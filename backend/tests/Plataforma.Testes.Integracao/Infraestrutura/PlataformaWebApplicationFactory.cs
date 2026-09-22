using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Notificacoes;
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
                ["Verificacao:ChaveHmac"] = "chave-hmac-de-teste-0123456789-0123456789",
            });
        });

        // Espiões no lugar dos provedores Fake (seção 4) — os testes de verificação (seção
        // 8.1) precisam ler o código "enviado" para validar o passo seguinte do fluxo, o que
        // o Fake normal (só loga) não permite de forma prática. ConfigureServices (não
        // ConfigureTestServices, que exigiria o pacote Microsoft.AspNetCore.TestHost) roda
        // depois do ConfigureServices do próprio Program.cs, então este registro vence.
        builder.ConfigureServices(servicos =>
        {
            servicos.AddSingleton<EspiaEmail>();
            servicos.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<EspiaEmail>());

            servicos.AddSingleton<EspiaWhatsApp>();
            servicos.AddSingleton<IMensageriaWhatsApp>(sp => sp.GetRequiredService<EspiaWhatsApp>());
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
