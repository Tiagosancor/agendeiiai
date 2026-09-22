using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>
/// Estende o <see cref="PlataformaDbContext"/> só para o teste, acrescentando
/// <see cref="ItemDeTeste"/> ao modelo. O EF Core descobre <c>DbSet</c> pela classe em
/// tempo de execução (<c>this.GetType()</c>), então isso não exige mudar o contexto real.
/// </summary>
public sealed class DbContextDeTeste : PlataformaDbContext
{
    public DbContextDeTeste(DbContextOptions<PlataformaDbContext> options, IContextoNegocio contextoNegocio)
        : base(options, contextoNegocio)
    {
    }

    public DbSet<ItemDeTeste> Itens => Set<ItemDeTeste>();
}
