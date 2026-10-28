using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Retenção do diagnóstico do agente: apaga de <c>rodadas_agente</c> o que passou de <c>Ai__RetencaoRodadasDias</c> (padrão 90), em lotes.
/// Só essa tabela — <c>usos_ia</c> é a base da cota mensal e dos limites e fica. Varre todos os negócios (job sem tenant, como os
/// demais). Registra apenas a quantidade removida.
/// </summary>
public sealed class JobLimparRodadasAgente
{
    public const int TamanhoDoLote = 1000;

    private readonly PlataformaDbContext _dbContext;
    private readonly OpcoesIa _opcoes;
    private readonly ILogger<JobLimparRodadasAgente> _logger;

    public JobLimparRodadasAgente(PlataformaDbContext dbContext, IOptions<OpcoesIa> opcoes, ILogger<JobLimparRodadasAgente> logger)
    {
        _dbContext = dbContext;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public Task<int> ExecutarAsync(CancellationToken cancellationToken = default) =>
        ExecutarAsync(DateTimeOffset.UtcNow, cancellationToken);

    public async Task<int> ExecutarAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var limite = agora.AddDays(-_opcoes.RetencaoRodadasDias);
        var removidas = 0;
        while (true)
        {
            var ids = await _dbContext.RodadasAgente.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.IniciadaEm < limite)
                .OrderBy(r => r.IniciadaEm)
                .Select(r => r.Id)
                .Take(TamanhoDoLote)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
                break;

            removidas += await _dbContext.RodadasAgente.IgnoreQueryFilters()
                .Where(r => ids.Contains(r.Id) && r.IniciadaEm < limite)
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (removidas > 0)
            _logger.LogInformation("Agente: {Removidas} rodada(s) antiga(s) removida(s) do diagnóstico.", removidas);

        return removidas;
    }
}
