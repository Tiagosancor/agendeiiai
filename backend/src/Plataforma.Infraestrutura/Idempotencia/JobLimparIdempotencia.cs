using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma.Dominio.Idempotencia;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Idempotencia;

/// <summary>
/// Apaga as chaves expiradas de todos os negócios, em lotes de até 1.000 (só os Ids passam pela memória). Único ponto que usa
/// <c>IgnoreQueryFilters</c> nesta tabela. Registra apenas a quantidade removida — nunca chave, hash ou resultado.
/// </summary>
public sealed class JobLimparIdempotencia
{
    public const int TamanhoDoLote = 1000;

    private readonly PlataformaDbContext _dbContext;
    private readonly ILogger<JobLimparIdempotencia> _logger;

    public JobLimparIdempotencia(PlataformaDbContext dbContext, ILogger<JobLimparIdempotencia> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int> ExecutarAsync(CancellationToken cancellationToken = default)
    {
        var removidas = 0;
        while (true)
        {
            var agora = DateTimeOffset.UtcNow;
            var ids = await _dbContext.Set<RequisicaoIdempotente>().IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.ExpiraEm < agora)
                .OrderBy(r => r.ExpiraEm)
                .Select(r => r.Id)
                .Take(TamanhoDoLote)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
                break;

            removidas += await _dbContext.Set<RequisicaoIdempotente>().IgnoreQueryFilters()
                .Where(r => ids.Contains(r.Id) && r.ExpiraEm < agora)
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (removidas > 0)
            _logger.LogInformation("Idempotência: {Removidas} chave(s) expirada(s) removida(s).", removidas);

        return removidas;
    }
}
