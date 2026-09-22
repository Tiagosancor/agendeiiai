using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;

namespace Plataforma.Infraestrutura.Persistencia;

/// <summary>
/// Contexto EF Core único do monólito modular. Aplica automaticamente um
/// *global query filter* por <c>NegocioId</c> em toda entidade que implemente
/// <see cref="IEntidadeDoNegocio"/> (seção 8.3.1) — sem negócio resolvido no
/// <see cref="IContextoNegocio"/>, nenhuma linha dessas tabelas é visível
/// (comportamento seguro por padrão; use <c>IgnoreQueryFilters()</c> apenas em
/// rotinas administrativas explícitas e revisadas).
/// </summary>
public class PlataformaDbContext : DbContext
{
    private static readonly MethodInfo MetodoAplicarFiltroDoNegocio = typeof(PlataformaDbContext)
        .GetMethod(nameof(AplicarFiltroDoNegocio), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly IContextoNegocio _contextoNegocio;

    public PlataformaDbContext(DbContextOptions<PlataformaDbContext> options, IContextoNegocio contextoNegocio)
        : base(options)
    {
        _contextoNegocio = contextoNegocio;
    }

    public DbSet<Negocio> Negocios => Set<Negocio>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Pré-requisito da exclusion constraint de horários (seção 8.2.1) — confirmado
        // também em tempo de execução pelo health check de btree_gist (seção 8.5.5).
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlataformaDbContext).Assembly);

        // O modelo do EF Core é montado uma vez e reaproveitado entre instâncias do
        // DbContext (cada requisição ganha uma instância nova, mas o *modelo* é cacheado).
        // Por isso o filtro é escrito como lambda de um MÉTODO DE INSTÂNCIA (não como
        // Expression.Constant fechando sobre um valor fixo): o EF Core reconhece o
        // fechamento sobre "this" e reavalia _contextoNegocio.NegocioId a cada consulta,
        // usando a instância do DbContext (e portanto o negócio) da requisição atual.
        foreach (var tipoEntidade in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (!typeof(IEntidadeDoNegocio).IsAssignableFrom(tipoEntidade.ClrType))
                continue;

            MetodoAplicarFiltroDoNegocio
                .MakeGenericMethod(tipoEntidade.ClrType)
                .Invoke(this, [modelBuilder]);
        }

        base.OnModelCreating(modelBuilder);
    }

    private void AplicarFiltroDoNegocio<TEntidade>(ModelBuilder modelBuilder)
        where TEntidade : class, IEntidadeDoNegocio
    {
        modelBuilder.Entity<TEntidade>().HasQueryFilter(e => e.NegocioId == _contextoNegocio.NegocioId);
    }

    public override int SaveChanges()
    {
        AtualizarCarimbosDeAuditoria();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AtualizarCarimbosDeAuditoria();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void AtualizarCarimbosDeAuditoria()
    {
        var agora = DateTimeOffset.UtcNow;

        foreach (var entrada in ChangeTracker.Entries<EntidadeBase>())
        {
            if (entrada.State == EntityState.Modified)
                entrada.Entity.AtualizadoEm = agora;
        }
    }
}
