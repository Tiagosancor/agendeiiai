using Testcontainers.PostgreSql;
using Xunit;

namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>
/// Sobe um PostgreSQL real (Testcontainers) uma vez para toda a coleção de testes de
/// integração — bem mais rápido que um container por teste, e ainda é o banco de
/// verdade (não InMemory/SQLite), como a seção 4 pede para os testes do backend.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("plataforma_testes")
        .WithUsername("plataforma_testes")
        .WithPassword("plataforma_testes")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(NomeColecao)]
public sealed class ColecaoComPostgres : ICollectionFixture<PostgresContainerFixture>
{
    public const string NomeColecao = "Postgres";
}
