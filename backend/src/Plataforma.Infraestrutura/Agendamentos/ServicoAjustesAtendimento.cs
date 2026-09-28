using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Comissoes;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Ajuste de valor durante o atendimento (seção 7). O preço original nunca muda; o valor ajustado
/// vai para o total (pagamento), o faturamento e a base da comissão. Corrigir depois de concluído é
/// só do Administrador, recalcula a comissão da linha e passa pela trava de quinzenas.
/// </summary>
public sealed class ServicoAjustesAtendimento : IServicoAjustesAtendimento
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IRegistroAuditoria _auditoria;
    private readonly TravaQuinzenas _travaQuinzenas;

    public ServicoAjustesAtendimento(
        PlataformaDbContext dbContext, IUsuarioAtual usuarioAtual, IRegistroAuditoria auditoria, TravaQuinzenas travaQuinzenas)
    {
        _dbContext = dbContext;
        _usuarioAtual = usuarioAtual;
        _auditoria = auditoria;
        _travaQuinzenas = travaQuinzenas;
    }

    public Task<ResultadoAjusteValor> AjustarAsync(Guid agendamentoId, Guid linhaId, AjustarValor dados, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<TipoAjusteValor>(dados.Tipo, ignoreCase: true, out var tipo) || !Enum.IsDefined(tipo)
            || !Enum.TryParse<ModoAjusteValor>(dados.Modo, ignoreCase: true, out var modo) || !Enum.IsDefined(modo))
            return Task.FromResult(ResultadoAjusteValor.Falha(ErroAjusteValor.DadosInvalidos, "Escolha desconto ou acréscimo, em R$ ou %."));

        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        return estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await _travaQuinzenas.TravarNegocioAsync(cancellationToken);

            var agendamento = await _dbContext.Agendamentos.Include(a => a.Servicos)
                .FirstOrDefaultAsync(a => a.Id == agendamentoId, cancellationToken);
            if (agendamento is null || agendamento.Servicos.All(s => s.Id != linhaId))
                return ResultadoAjusteValor.Falha(ErroAjusteValor.NaoEncontrado, "Atendimento não encontrado.");

            var usuario = await UsuarioAtualAsync(cancellationToken);
            var correcao = agendamento.Status == StatusAgendamento.Concluido;
            if (usuario is null || !PodeAjustar(usuario, agendamento))
                return ResultadoAjusteValor.Falha(ErroAjusteValor.SemAcesso, correcao
                    ? "Depois de concluído, só o Administrador corrige o valor."
                    : "Você só pode ajustar os valores dos seus próprios atendimentos.");

            if (agendamento.Status is not (StatusAgendamento.EmAtendimento or StatusAgendamento.Concluido))
                return ResultadoAjusteValor.Falha(ErroAjusteValor.StatusInvalido,
                    "O valor só pode ser ajustado com o atendimento em andamento. Toque em \"Iniciar atendimento\" antes.");

            await _travaQuinzenas.GarantirNaoTravadoAsync(agendamento.ProfissionalId, agendamento.Inicio, cancellationToken);

            AjusteValorAtendimento ajuste;
            try
            {
                ajuste = agendamento.AjustarValor(linhaId, tipo, modo, dados.Valor, dados.Motivo ?? string.Empty,
                    usuario.Id, DateTimeOffset.UtcNow, correcaoAposConclusao: correcao);
            }
            catch (ArgumentException excecao)
            {
                return ResultadoAjusteValor.Falha(ErroAjusteValor.DadosInvalidos, MensagemSemParametro(excecao));
            }

            _dbContext.AjustesValorAtendimento.Add(ajuste);
            var linha = agendamento.Servicos.First(s => s.Id == linhaId);
            _auditoria.Registrar(correcao ? AcoesAuditoria.CorrigirValorAtendimento : AcoesAuditoria.AjustarValorAtendimento,
                "Agendamento", agendamento.Id,
                $"{linha.Nome}: {FormatacaoBrasil.Reais(ajuste.ValorAntes)} → {FormatacaoBrasil.Reais(ajuste.ValorDepois)} "
                + $"({Descrever(ajuste)}). Motivo: {ajuste.Motivo}");

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return ResultadoAjusteValor.Ok(ajuste.ValorDepois);
        });
    }

    public async Task<ValoresAtendimento?> ObterValoresAsync(Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var agendamento = await _dbContext.Agendamentos.AsNoTracking().Include(a => a.Servicos)
            .FirstOrDefaultAsync(a => a.Id == agendamentoId, cancellationToken);
        var usuario = await UsuarioAtualAsync(cancellationToken);
        if (agendamento is null || usuario is null)
            return null;

        // Quem gerencia a agenda vê qualquer atendimento; o profissional, só os dele (o valor ajustado mexe na
        // comissão dele) — mesmo com a permissão de ajustar, que para ele vale só nos próprios.
        var ehDele = usuario.ProfissionalId == agendamento.ProfissionalId;
        var podeVer = ehDele || usuario.TemPermissao(Permissao.GerenciarAgenda)
            || (usuario.TemPermissao(Permissao.AjustarValorAtendimento) && usuario.Perfil != Perfil.Profissional);
        if (!podeVer)
            return null;

        var ajustes = await _dbContext.AjustesValorAtendimento.AsNoTracking()
            .Where(a => a.AgendamentoId == agendamentoId)
            .OrderBy(a => a.CriadoEm)
            .ToListAsync(cancellationToken);
        var idsUsuarios = ajustes.Where(a => a.UsuarioId != null).Select(a => a.UsuarioId!.Value).Distinct().ToList();
        var nomes = await _dbContext.Usuarios.AsNoTracking().Where(u => idsUsuarios.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nome, cancellationToken);

        var linhas = agendamento.Servicos
            .OrderBy(s => s.Nome)
            .Select(s => new LinhaValores(s.Id, s.Nome, s.Preco, s.ValorCobrado, ajustes
                .Where(a => a.AgendamentoServicoId == s.Id)
                .Select(a => new AjusteRegistrado(a.CriadoEm, a.Tipo.ToString(), a.Modo.ToString(), a.ValorInformado, a.ValorAntes,
                    a.ValorDepois, a.Motivo, a.UsuarioId is { } u && nomes.TryGetValue(u, out var nome) ? nome : null, a.AposConclusao))
                .ToList()))
            .ToList();

        var podeAjustar = usuario.TemPermissao(Permissao.AjustarValorAtendimento) && PodeAjustar(usuario, agendamento)
            && agendamento.Status is StatusAgendamento.EmAtendimento or StatusAgendamento.Concluido;

        return new ValoresAtendimento(agendamento.Id, agendamento.Status.ToString(), agendamento.DescontoAplicado, agendamento.Total, podeAjustar, linhas);
    }

    /// <summary>
    /// Alcance (seção 7): com o atendimento aberto, o Profissional só os dele e os demais perfis qualquer
    /// um; concluído, só o Administrador. A permissão em si vem da policy (e é conferida de novo aqui
    /// para a tela saber se mostra o botão).
    /// </summary>
    private static bool PodeAjustar(Usuario usuario, Agendamento agendamento) =>
        agendamento.Status == StatusAgendamento.Concluido
            ? usuario.Perfil == Perfil.Administrador
            : usuario.Perfil != Perfil.Profissional || usuario.ProfissionalId == agendamento.ProfissionalId;

    private async Task<Usuario?> UsuarioAtualAsync(CancellationToken cancellationToken) =>
        _usuarioAtual.UsuarioId is { } id
            ? await _dbContext.Usuarios.AsNoTracking().Include(u => u.Permissoes).FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            : null;

    private static string Descrever(AjusteValorAtendimento ajuste)
    {
        var tipo = ajuste.Tipo == TipoAjusteValor.Desconto ? "desconto" : "acréscimo";
        var valor = ajuste.Modo == ModoAjusteValor.Reais
            ? FormatacaoBrasil.Reais(ajuste.ValorInformado)
            : ajuste.ValorInformado.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + "%";
        return $"{tipo} de {valor}";
    }

    private static string MensagemSemParametro(ArgumentException excecao) =>
        excecao.ParamName is null ? excecao.Message : excecao.Message.Replace($" (Parameter '{excecao.ParamName}')", string.Empty);
}
