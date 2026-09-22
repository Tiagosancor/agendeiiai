using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Respawn;

namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>
/// Sobe a API inteira (<c>Program</c>) apontando para o Postgres do
/// <see cref="PostgresContainerFixture"/>. As migrations rodam sozinhas no startup
/// porque o ambiente é "Development" (mesmo comportamento de <c>docker compose up</c>).
/// </summary>
public sealed class PlataformaWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private Respawner? _respawner;

    public PlataformaWebApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuracao) =>
        {
            // Domínio neutro, sem relação com o nome do produto — os testes provam que a
            // resolução por subdomínio funciona para QUALQUER marca configurada.
            configuracao.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Padrao"] = _connectionString,
                ["Marca:NomeProduto"] = "Plataforma de Testes",
                ["Marca:Dominio"] = DominioDeTeste.Valor,
                ["Marca:EmailRemetente"] = $"nao-responda@{DominioDeTeste.Valor}",
                ["Jwt:ChaveSecreta"] = "chave-secreta-de-teste-0123456789-0123456789-0123456789",
                ["Jwt:Emissor"] = "plataforma-testes",
                ["Jwt:Audiencia"] = "plataforma-testes-painel",
                ["Cpf:ChaveId"] = "teste-v1",
                ["Cpf:ChaveBase64"] = Convert.ToBase64String(new byte[32]), // chave zerada — só para teste
            });
        });
    }

    /// <summary>Limpa todas as tabelas entre testes, preservando o schema (migrations não rodam de novo).</summary>
    public async Task ResetarBancoAsync()
    {
        await using var conexao = new NpgsqlConnection(_connectionString);
        await conexao.OpenAsync();

        _respawner ??= await Respawner.CreateAsync(conexao, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"],
        });

        await _respawner.ResetAsync(conexao);
    }
}
