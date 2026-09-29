using Microsoft.EntityFrameworkCore;
using Plataforma.Dominio.Estoque;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Estoque;

/// <summary>
/// Trava a linha do produto (<c>FOR UPDATE</c>) dentro da transação do chamador e devolve o produto com os dados
/// lidos <b>depois</b> da trava: duas saídas simultâneas no limite do estoque são enfileiradas, e a segunda já vê o
/// que sobrou (mesma cautela da seção 8.2). O check do banco (quantidade ≥ 0) é a última barreira.
/// </summary>
internal static class TravaProduto
{
    public static async Task<Produto?> TravarAsync(
        PlataformaDbContext dbContext, Guid negocioId, Guid produtoId, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM produtos WHERE id = {produtoId} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);

        var produto = await dbContext.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId, cancellationToken);
        if (produto is not null)
            await dbContext.Entry(produto).ReloadAsync(cancellationToken);
        return produto;
    }
}
