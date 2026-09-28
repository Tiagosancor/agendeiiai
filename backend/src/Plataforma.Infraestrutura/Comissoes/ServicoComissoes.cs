using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Profissionais;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Comissoes;

/// <summary>
/// Comissões (seção 7). Só entram linhas de atendimento <c>Concluido</c> com comissão gravada
/// — os concluídos antes deste recurso não têm e não ganham retroativamente. Comissão é dado de
/// remuneração: nada daqui vai para log de aplicação, e-mail ou página pública.
/// </summary>
public sealed class ServicoComissoes : IServicoComissoes
{
    public const int TamanhoMaximoPagina = 100;

    private readonly PlataformaDbContext _dbContext;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IRegistroAuditoria _auditoria;
    private readonly TravaQuinzenas _travaQuinzenas;

    public ServicoComissoes(
        PlataformaDbContext dbContext, IUsuarioAtual usuarioAtual, IRegistroAuditoria auditoria, TravaQuinzenas travaQuinzenas)
    {
        _dbContext = dbContext;
        _usuarioAtual = usuarioAtual;
        _auditoria = auditoria;
        _travaQuinzenas = travaQuinzenas;
    }

    public Task<ConfiguracaoComissao?> ObterConfiguracaoAsync(Guid profissionalId, CancellationToken cancellationToken = default) =>
        _dbContext.Profissionais
            .Where(p => p.Id == profissionalId && !p.Excluido)
            .Select(p => new ConfiguracaoComissao(p.PercentualComissao, p.AcertoPorQuinzena))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> DefinirConfiguracaoAsync(
        Guid profissionalId, ConfiguracaoComissao configuracao, CancellationToken cancellationToken = default)
    {
        var profissional = await _dbContext.Profissionais.FirstOrDefaultAsync(p => p.Id == profissionalId && !p.Excluido, cancellationToken);
        if (profissional is null)
            return false;

        var percentualAnterior = profissional.PercentualComissao;
        var acertoAnterior = profissional.AcertoPorQuinzena;
        profissional.DefinirPercentualComissao(configuracao.Percentual);
        profissional.DefinirAcertoPorQuinzena(configuracao.AcertoPorQuinzena);

        var mudancas = new List<string>();
        if (percentualAnterior != configuracao.Percentual)
            mudancas.Add($"Comissão: {Percentual(percentualAnterior)} → {Percentual(configuracao.Percentual)}");
        if (acertoAnterior != configuracao.AcertoPorQuinzena)
            mudancas.Add($"Acerto por quinzena: {SimNao(acertoAnterior)} → {SimNao(configuracao.AcertoPorQuinzena)}");

        if (mudancas.Count > 0)
        {
            _auditoria.Registrar(AcoesAuditoria.AlterarComissao, nameof(Profissional), profissional.Id, string.Join("; ", mudancas));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<ComissoesDoProfissional> ListarMinhasAsync(FiltroComissoes filtro, CancellationToken cancellationToken = default)
    {
        var usuarioId = _usuarioAtual.UsuarioId ?? throw new InvalidOperationException("Sem usuário logado.");
        var profissionalId = await _dbContext.Usuarios
            .Where(u => u.Id == usuarioId)
            .Select(u => u.ProfissionalId)
            .FirstOrDefaultAsync(cancellationToken);

        var resultado = profissionalId is { } id ? await ListarDoProfissionalAsync(id, filtro, cancellationToken) : null;
        return resultado ?? new ComissoesDoProfissional(null, null, null, TotaisComissao.Zero, [], filtro.Pagina, filtro.TamanhoPagina, 0);
    }

    public async Task<ComissoesDoProfissional?> ListarDoProfissionalAsync(
        Guid profissionalId, FiltroComissoes filtro, CancellationToken cancellationToken = default)
    {
        var profissional = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Id == profissionalId)
            .Select(p => new { p.Id, p.Nome, p.PercentualComissao })
            .FirstOrDefaultAsync(cancellationToken);
        if (profissional is null)
            return null;

        var (inicioUtc, fimUtc) = await IntervaloUtcAsync(filtro.De, filtro.Ate, cancellationToken);
        var linhas = ConsultaLinhasComissao.Concluidas(_dbContext, inicioUtc, fimUtc).Where(l => l.ProfissionalId == profissionalId);

        var totais = await linhas
            .GroupBy(_ => 1)
            .Select(g => new TotaisComissao(g.Sum(l => l.Comissao), g.Sum(l => l.ValorBase), g.Count()))
            .FirstOrDefaultAsync(cancellationToken) ?? TotaisComissao.Zero;

        var pagina = await linhas
            .OrderByDescending(l => l.Inicio).ThenBy(l => l.LinhaId)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .ToListAsync(cancellationToken);

        var idsClientes = pagina.Where(l => l.ClienteId != null).Select(l => l.ClienteId!.Value).Distinct().ToList();
        var nomes = await _dbContext.Clientes.AsNoTracking()
            .Where(c => idsClientes.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nome, cancellationToken);

        var itens = pagina
            .Select(l => new ItemComissao(
                l.AgendamentoId, l.Inicio, l.Servico,
                PrimeiroNome(l.ClienteId is { } c && nomes.TryGetValue(c, out var nome) ? nome : null),
                l.ValorBase, l.Percentual, l.Comissao))
            .ToList();

        return new ComissoesDoProfissional(
            profissional.Id, profissional.Nome, profissional.PercentualComissao, totais,
            itens, filtro.Pagina, filtro.TamanhoPagina, totais.QuantidadeServicos);
    }

    public async Task<IReadOnlyList<ResumoComissaoProfissional>> ResumirPorProfissionalAsync(
        DateOnly de, DateOnly ate, CancellationToken cancellationToken = default)
    {
        var (inicioUtc, fimUtc) = await IntervaloUtcAsync(de, ate, cancellationToken);

        var porProfissional = await ConsultaLinhasComissao.Concluidas(_dbContext, inicioUtc, fimUtc)
            .GroupBy(l => l.ProfissionalId)
            .Select(g => new { ProfissionalId = g.Key, Comissao = g.Sum(l => l.Comissao), Base = g.Sum(l => l.ValorBase), Quantidade = g.Count() })
            .ToDictionaryAsync(g => g.ProfissionalId, cancellationToken);

        // Todo mundo em atividade aparece (com zero, se for o caso); excluído só se atendeu no período.
        var ids = porProfissional.Keys.ToList();
        var profissionais = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => !p.Excluido || ids.Contains(p.Id))
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome, p.Ativo, p.PercentualComissao })
            .ToListAsync(cancellationToken);

        return profissionais
            .Select(p => new ResumoComissaoProfissional(
                p.Id, p.Nome, p.Ativo, p.PercentualComissao,
                porProfissional.TryGetValue(p.Id, out var t) ? new TotaisComissao(t.Comissao, t.Base, t.Quantidade) : TotaisComissao.Zero))
            .ToList();
    }

    private async Task<(DateTimeOffset InicioUtc, DateTimeOffset FimUtc)> IntervaloUtcAsync(
        DateOnly de, DateOnly ate, CancellationToken cancellationToken) =>
        ConsultaLinhasComissao.IntervaloUtc(de, ate, await _travaQuinzenas.FusoAsync(cancellationToken));

    private static string PrimeiroNome(string? nome)
    {
        var primeiro = nome?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(primeiro) ? "Cliente" : primeiro;
    }

    private static string SimNao(bool valor) => valor ? "sim" : "não";

    private static string Percentual(decimal valor) =>
        valor.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') + "%";
}
