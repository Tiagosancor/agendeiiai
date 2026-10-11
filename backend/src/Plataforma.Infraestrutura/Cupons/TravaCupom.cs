using Microsoft.EntityFrameworkCore;
using Plataforma.Dominio.Cupons;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Cupons;

/// <summary>
/// Trava a linha do cupom (<c>FOR UPDATE</c>) dentro da transação do chamador e devolve o cupom com os dados lidos
/// <b>depois</b> da trava. Confirmações que disputam o último uso, a desativação e a mudança de limite ficam enfileiradas:
/// quem chega depois já vê o contador, o limite e o "ativo" que o outro gravou, e a regra continua toda no domínio
/// (<see cref="Cupom.TentarAplicar"/>). Ordem das travas: a linha do agendamento antes da do cupom, nunca o contrário.
/// </summary>
internal static class TravaCupom
{
    public static async Task<Cupom?> TravarPorCodigoAsync(
        PlataformaDbContext dbContext, Guid negocioId, string codigo, CancellationToken cancellationToken)
    {
        var normalizado = codigo.Trim().ToUpperInvariant();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM cupons WHERE negocio_id = {negocioId} AND codigo = {normalizado} FOR UPDATE", cancellationToken);

        return await RelerAsync(
            dbContext, await dbContext.Cupons.FirstOrDefaultAsync(c => c.NegocioId == negocioId && c.Codigo == normalizado, cancellationToken),
            cancellationToken);
    }

    public static async Task<Cupom?> TravarPorIdAsync(
        PlataformaDbContext dbContext, Guid negocioId, Guid cupomId, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM cupons WHERE id = {cupomId} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);

        return await RelerAsync(
            dbContext, await dbContext.Cupons.FirstOrDefaultAsync(c => c.Id == cupomId, cancellationToken), cancellationToken);
    }

    /// <summary>Uma instância já rastreada voltaria com os valores de antes da trava (identity resolution) — por isso relê.</summary>
    private static async Task<Cupom?> RelerAsync(PlataformaDbContext dbContext, Cupom? cupom, CancellationToken cancellationToken)
    {
        if (cupom is not null)
            await dbContext.Entry(cupom).ReloadAsync(cancellationToken);
        return cupom;
    }
}
