using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Cadastros;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Servicos;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Servicos;

public sealed class GerenciadorServicos : IGerenciadorServicos
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IRegistroAuditoria _auditoria;

    public GerenciadorServicos(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IRegistroAuditoria auditoria)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _auditoria = auditoria;
    }

    public async Task<IReadOnlyList<ServicoResumo>> ListarAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Servicos
            .Where(s => !s.Excluido)
            .OrderBy(s => s.Nome)
            .Select(s => new ServicoResumo(s.Id, s.CategoriaId, s.Nome, s.Preco, s.DuracaoMinutos, s.Popular, s.Ativo, s.ExibirNaPaginaInicial))
            .ToListAsync(cancellationToken);

    public async Task<Guid> CriarAsync(CriarServico dados, CancellationToken cancellationToken = default)
    {
        var servico = Servico.Criar(
            _contextoNegocio.NegocioId!.Value, dados.CategoriaId, dados.Nome, dados.Preco, dados.DuracaoMinutos, dados.Popular,
            dados.ExibirNaPaginaInicial);

        _dbContext.Servicos.Add(servico);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return servico.Id;
    }

    /// <summary>
    /// Vale só para agendamentos novos (seção 7): os já marcados e concluídos guardam o nome,
    /// preço e duração do momento em <c>AgendamentoServico</c>, e o financeiro soma esse retrato.
    /// </summary>
    public async Task<bool> AtualizarAsync(Guid servicoId, AtualizarServico dados, CancellationToken cancellationToken = default)
    {
        var servico = await BuscarAsync(servicoId, cancellationToken);
        if (servico is null)
            return false;

        // Serviço não é dado pessoal: o log guarda o antes → depois, útil para conferir preço.
        var alteracoes = new List<string>();
        if (servico.Nome != dados.Nome.Trim()) alteracoes.Add($"nome: {servico.Nome} → {dados.Nome.Trim()}");
        if (servico.Preco != dados.Preco) alteracoes.Add($"preço: {FormatacaoBrasil.Reais(servico.Preco)} → {FormatacaoBrasil.Reais(dados.Preco)}");
        if (servico.DuracaoMinutos != dados.DuracaoMinutos) alteracoes.Add($"duração: {servico.DuracaoMinutos} → {dados.DuracaoMinutos} min");
        if (servico.CategoriaId != dados.CategoriaId) alteracoes.Add("categoria");
        if (servico.Popular != dados.Popular) alteracoes.Add($"popular: {(dados.Popular ? "sim" : "não")}");
        if (dados.ExibirNaPaginaInicial is { } exibir && servico.ExibirNaPaginaInicial != exibir)
            alteracoes.Add($"na página do negócio: {(exibir ? "sim" : "não")}");

        servico.AtualizarDados(dados.CategoriaId, dados.Nome, dados.Preco, dados.DuracaoMinutos, dados.Popular);
        if (dados.ExibirNaPaginaInicial is { } exibicao)
            servico.DefinirExibicaoNaPaginaInicial(exibicao);

        if (alteracoes.Count > 0)
            _auditoria.Registrar(AcoesAuditoria.Editar, nameof(Servico), servico.Id, string.Join("; ", alteracoes));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DesativarAsync(Guid servicoId, CancellationToken cancellationToken = default) =>
        await AlterarAtivoAsync(servicoId, ativo: false, cancellationToken);

    public async Task<bool> AtivarAsync(Guid servicoId, CancellationToken cancellationToken = default) =>
        await AlterarAtivoAsync(servicoId, ativo: true, cancellationToken);

    public async Task<PreviaExclusao?> ObterPreviaExclusaoAsync(Guid servicoId, CancellationToken cancellationToken = default)
    {
        var servico = await BuscarAsync(servicoId, cancellationToken);
        if (servico is null)
            return null;

        var agora = DateTimeOffset.UtcNow;
        var futuros = await _dbContext.Agendamentos.CountAsync(
            a => a.Inicio > agora && (a.Status == StatusAgendamento.Agendado || a.Status == StatusAgendamento.EmAtendimento) && a.Servicos.Any(s => s.ServicoId == servicoId),
            cancellationToken);

        return new PreviaExclusao(servico.Nome, await TemHistoricoAsync(servicoId, cancellationToken), futuros, null, []);
    }

    public async Task<ResultadoExclusao> ExcluirAsync(Guid servicoId, CancellationToken cancellationToken = default)
    {
        var servico = await BuscarAsync(servicoId, cancellationToken);
        if (servico is null)
            return ResultadoExclusao.NaoEncontrado;

        var temHistorico = await TemHistoricoAsync(servicoId, cancellationToken);

        // Sai da oferta de todos os profissionais na hora.
        _dbContext.ProfissionalServicos.RemoveRange(
            await _dbContext.ProfissionalServicos.Where(ps => ps.ServicoId == servicoId).ToListAsync(cancellationToken));

        if (temHistorico)
        {
            servico.Excluir(DateTimeOffset.UtcNow);
            _auditoria.Registrar(AcoesAuditoria.ExcluirLogicamente, nameof(Servico), servico.Id, $"Nome: {servico.Nome}");
        }
        else
        {
            _dbContext.Servicos.Remove(servico);
            _auditoria.Registrar(AcoesAuditoria.ApagarDefinitivo, nameof(Servico), servico.Id, $"Nome: {servico.Nome}");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ResultadoExclusao.Concluido(temHistorico);
    }

    private async Task<bool> AlterarAtivoAsync(Guid servicoId, bool ativo, CancellationToken cancellationToken)
    {
        var servico = await BuscarAsync(servicoId, cancellationToken);
        if (servico is null)
            return false;

        if (ativo)
            servico.Ativar();
        else
            servico.Desativar();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Já entrou em algum agendamento (de qualquer status) = tem histórico (seção 7).</summary>
    private Task<bool> TemHistoricoAsync(Guid servicoId, CancellationToken cancellationToken) =>
        _dbContext.Agendamentos.AnyAsync(a => a.Servicos.Any(s => s.ServicoId == servicoId), cancellationToken);

    private Task<Servico?> BuscarAsync(Guid servicoId, CancellationToken cancellationToken) =>
        _dbContext.Servicos.FirstOrDefaultAsync(s => s.Id == servicoId && !s.Excluido, cancellationToken);
}
