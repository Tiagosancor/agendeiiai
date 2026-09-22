using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.Usuarios;

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

    public DbSet<Usuario> Usuarios => Set<Usuario>();

    public DbSet<TokenAtualizacao> TokensAtualizacao => Set<TokenAtualizacao>();

    public DbSet<Profissional> Profissionais => Set<Profissional>();

    public DbSet<Categoria> Categorias => Set<Categoria>();

    public DbSet<Servico> Servicos => Set<Servico>();

    public DbSet<Cliente> Clientes => Set<Cliente>();

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
            if (typeof(EntidadeBase).IsAssignableFrom(tipoEntidade.ClrType))
            {
                // O Id é sempre gerado no construtor da entidade (Guid.NewGuid()), nunca
                // pelo banco. Sem isso, o EF Core, ao descobrir uma entidade nova só por
                // navegação (ex.: Usuario.ConcederPermissao adicionando um UsuarioPermissao
                // a uma coleção já rastreada, sem passar por Add() explícito), vê uma chave
                // "não-padrão" e conclui — errado — que a linha já existe, gerando UPDATE em
                // vez de INSERT (e um DbUpdateConcurrencyException, já que a linha não existe).
                modelBuilder.Entity(tipoEntidade.ClrType)
                    .Property(nameof(EntidadeBase.Id))
                    .ValueGeneratedNever();
            }

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
