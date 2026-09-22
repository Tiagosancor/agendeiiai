using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Plataforma.Infraestrutura.Persistencia;

/// <summary>
/// Confere se a extensão <c>btree_gist</c> está habilitada no banco — pré-requisito da
/// *exclusion constraint* que impede horários sobrepostos (seção 8.2.1). Sem ela, uma
/// migration falharia silenciosamente em proteger a regra mais crítica do produto, então
/// checamos isso já no <c>/health</c> desde a Sprint 0 (seção 8.5.5).
/// </summary>
public sealed class VerificacaoExtensaoBtreeGistHealthCheck : IHealthCheck
{
    private readonly PlataformaDbContext _dbContext;

    public VerificacaoExtensaoBtreeGistHealthCheck(PlataformaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT 1 FROM pg_extension WHERE extname = 'btree_gist'";

        await using var comando = _dbContext.Database.GetDbConnection().CreateCommand();
        comando.CommandText = sql;

        var precisaAbrir = comando.Connection!.State != System.Data.ConnectionState.Open;
        if (precisaAbrir)
            await comando.Connection!.OpenAsync(cancellationToken);

        try
        {
            var resultado = await comando.ExecuteScalarAsync(cancellationToken);
            return resultado is not null
                ? HealthCheckResult.Healthy("Extensão btree_gist habilitada.")
                : HealthCheckResult.Unhealthy(
                    "Extensão btree_gist ausente. Rode 'CREATE EXTENSION btree_gist;' no banco.");
        }
        finally
        {
            if (precisaAbrir)
                await comando.Connection!.CloseAsync();
        }
    }
}
