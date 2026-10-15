using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Notificacoes.Outbox;

/// <summary>
/// Leitura administrativa da outbox. Lê todos os negócios de propósito (<c>IgnoreQueryFilters</c>): só é alcançável pelo esquema de
/// autenticação da plataforma. A projeção nunca inclui nada além de ids, tipo, canal, estado, contagens e códigos técnicos.
/// </summary>
public sealed class ConsultaOutboxNotificacoes : IConsultaOutboxNotificacoes
{
    public const int TamanhoMaximoDaPagina = 200;

    private const int QuantidadeDeFalhasRecentes = 20;

    private readonly PlataformaDbContext _dbContext;
    private readonly TimeProvider _relogio;

    public ConsultaOutboxNotificacoes(PlataformaDbContext dbContext, TimeProvider relogio)
    {
        _dbContext = dbContext;
        _relogio = relogio;
    }

    private IQueryable<NotificacaoPendente> Todas() => _dbContext.NotificacoesPendentes.IgnoreQueryFilters().AsNoTracking();

    public async Task<ResumoOutbox> ObterResumoAsync(CancellationToken cancellationToken = default)
    {
        var agora = _relogio.GetUtcNow();

        var contagens = await Todas()
            .GroupBy(n => n.Status)
            .Select(g => new { Status = g.Key, Quantidade = g.Count() })
            .ToListAsync(cancellationToken);
        int Contar(StatusNotificacao status) => contagens.FirstOrDefault(c => c.Status == status)?.Quantidade ?? 0;

        var maisAntiga = await Todas()
            .Where(n => n.Status == StatusNotificacao.Pendente)
            .OrderBy(n => n.CriadoEm)
            .Select(n => (DateTimeOffset?)n.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

        var recentes = await Projetar(Todas()
                .Where(n => n.Status == StatusNotificacao.Falhou || n.Status == StatusNotificacao.Desconhecida)
                .OrderByDescending(n => n.AtualizadoEm).ThenBy(n => n.Id)
                .Take(QuantidadeDeFalhasRecentes))
            .ToListAsync(cancellationToken);

        return new ResumoOutbox(
            Contar(StatusNotificacao.Pendente), Contar(StatusNotificacao.Enviando), Contar(StatusNotificacao.Falhou),
            Contar(StatusNotificacao.Desconhecida), maisAntiga is { } desde ? (agora - desde).TotalSeconds : null, maisAntiga, agora, recentes);
    }

    public async Task<PaginaOutbox> ListarAsync(FiltroOutbox filtro, CancellationToken cancellationToken = default)
    {
        var pagina = Math.Max(1, filtro.Pagina);
        var tamanho = Math.Clamp(filtro.Tamanho, 1, TamanhoMaximoDaPagina);

        var consulta = Todas();
        if (Enum.TryParse<StatusNotificacao>(filtro.Status, ignoreCase: true, out var status))
            consulta = consulta.Where(n => n.Status == status);
        if (Enum.TryParse<TipoNotificacao>(filtro.Tipo, ignoreCase: true, out var tipo))
            consulta = consulta.Where(n => n.Tipo == tipo);
        if (Enum.TryParse<CanalNotificacao>(filtro.Canal, ignoreCase: true, out var canal))
            consulta = consulta.Where(n => n.Canal == canal);
        if (filtro.NegocioId is { } negocioId)
            consulta = consulta.Where(n => n.NegocioId == negocioId);
        if (filtro.De is { } de)
        {
            var inicio = de.ToUniversalTime();
            consulta = consulta.Where(n => n.AtualizadoEm >= inicio);
        }

        if (filtro.Ate is { } ate)
        {
            var fim = ate.ToUniversalTime();
            consulta = consulta.Where(n => n.AtualizadoEm < fim);
        }

        var total = await consulta.CountAsync(cancellationToken);
        var itens = await Projetar(consulta
                .OrderByDescending(n => n.AtualizadoEm).ThenBy(n => n.Id)
                .Skip((pagina - 1) * tamanho)
                .Take(tamanho))
            .ToListAsync(cancellationToken);

        return new PaginaOutbox(itens, pagina, tamanho, total);
    }

    private static IQueryable<ItemOutbox> Projetar(IQueryable<NotificacaoPendente> consulta) => consulta.Select(n => new ItemOutbox(
        n.Id, n.NegocioId, n.Tipo.ToString(), n.Canal.ToString(), n.Status.ToString(), n.Tentativas, n.CriadoEm, n.AtualizadoEm,
        n.Status == StatusNotificacao.Pendente ? n.ProximaTentativaEm : null, n.ExpiraEm, n.EnviadaEm, n.CanceladaEm,
        n.UltimoErroCodigo, n.AgendamentoId));
}
