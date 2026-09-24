using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Administracao;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Assinaturas;

/// <summary>Tela de assinatura do painel e administração da plataforma (seção 7 / 8.6.7, ajuste 4d).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AdministracaoETelaAssinaturaTestes : IAsyncLifetime
{
    private const string EmailDono = "dono@plataforma.test";
    private const string SenhaDono = "SenhaDoDono123";

    // A API devolve enums como texto ("Suspensa") — o cliente do teste precisa ler igual.
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public AdministracaoETelaAssinaturaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();

        using var escopo = _fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<IAdministracaoPlataforma>()
            .CriarOuRedefinirAdministradorAsync(EmailDono, "Dono", SenhaDono);
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private async Task<HttpClient> ClienteDaPlataformaAsync()
    {
        using var anonimo = _fabrica.CreateClient();
        var resposta = await anonimo.PostAsJsonAsync("/plataforma/auth/login", new { email = EmailDono, senha = SenhaDono });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();

        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    private async Task DarAssinaturaAsync(Guid negocioId, string plano = "Ritmo", int diasDesdeInicioDoTeste = 0)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var p = await db.Planos.FirstAsync(x => x.Nome == plano);
        var assinatura = ServicoAssinatura.IniciarTeste(negocioId, p, Periodicidade.Mensal, "teste", DateTimeOffset.UtcNow.AddDays(-diasDesdeInicioDoTeste));
        ServicoAssinatura.AtualizarPorTempo(assinatura, DateTimeOffset.UtcNow);
        db.Assinaturas.Add(assinatura);
        await db.SaveChangesAsync();
    }

    private async Task<EstadoAssinatura> EstadoAsync(Guid negocioId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        return (await db.Assinaturas.IgnoreQueryFilters().SingleAsync(a => a.NegocioId == negocioId)).Estado;
    }

    [Fact]
    public async Task Usuario_de_negocio_nao_acessa_a_administracao_da_plataforma_nem_o_contrario()
    {
        var (clienteNegocio, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        using var clientePlataforma = await ClienteDaPlataformaAsync();

        (await clienteNegocio.GetAsync("/plataforma/negocios")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await clientePlataforma.GetAsync("/painel/profissionais")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var anonimo = _fabrica.CreateClient();
        (await anonimo.GetAsync("/plataforma/negocios")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Senha_errada_nao_entra_na_plataforma()
    {
        using var anonimo = _fabrica.CreateClient();

        var resposta = await anonimo.PostAsJsonAsync("/plataforma/auth/login", new { email = EmailDono, senha = "errada123" });

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Pagamento_manual_pela_plataforma_reativa_o_negocio_suspenso_e_fica_auditado()
    {
        var (clienteNegocio, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, diasDesdeInicioDoTeste: 45);
        (await clienteNegocio.GetAsync("/painel/profissionais")).StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        using var plataforma = await ClienteDaPlataformaAsync();
        var suspensos = await plataforma.GetFromJsonAsync<List<NegocioNaPlataforma>>("/plataforma/negocios?estado=Suspensa", OpcoesJson);
        suspensos!.Select(n => n.Id).Should().Contain(negocioId);

        var agora = DateTimeOffset.UtcNow;
        var resposta = await plataforma.PostAsJsonAsync($"/plataforma/negocios/{negocioId}/pagamentos",
            new PagamentoManual(79.90m, FormaCobranca.Pix, agora, agora, agora.AddMonths(1)), OpcoesJson);

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await EstadoAsync(negocioId)).Should().Be(EstadoAssinatura.Ativa);
        (await clienteNegocio.GetAsync("/painel/profissionais")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        (await db.LogsAuditoriaPlataforma.SingleAsync(l => l.NegocioId == negocioId))
            .Should().BeEquivalentTo(new { Autor = $"plataforma:{EmailDono}", Acao = "RegistrarPagamentoManual" });
    }

    [Fact]
    public async Task Estender_teste_suspender_e_reativar_passam_pelo_servico_e_ficam_no_historico()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, diasDesdeInicioDoTeste: 28);
        using var plataforma = await ClienteDaPlataformaAsync();

        (await plataforma.PostAsJsonAsync($"/plataforma/negocios/{negocioId}/estender-teste", new { dias = 15 })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await plataforma.PostAsJsonAsync($"/plataforma/negocios/{negocioId}/suspender", new { motivo = "Pedido do dono" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await EstadoAsync(negocioId)).Should().Be(EstadoAssinatura.Suspensa);
        (await plataforma.PostAsync($"/plataforma/negocios/{negocioId}/reativar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await EstadoAsync(negocioId)).Should().Be(EstadoAssinatura.EmTeste);

        var detalhe = await plataforma.GetFromJsonAsync<DetalheNegocioNaPlataforma>($"/plataforma/negocios/{negocioId}", OpcoesJson);
        detalhe!.Historico.Select(h => h.Motivo).Should().Contain(m => m.StartsWith("Teste estendido")).And.Contain("Pedido do dono").And.Contain("Reativada");
    }

    [Fact]
    public async Task Plataforma_nao_troca_para_plano_menor_com_profissionais_demais()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, plano: "Ritmo");
        await SemearProfissionaisAtivosAsync(negocioId, 5);
        using var plataforma = await ClienteDaPlataformaAsync();
        var comeco = await PlanoIdAsync("Começo");

        var resposta = await plataforma.PostAsJsonAsync($"/plataforma/negocios/{negocioId}/plano", new { planoId = comeco, periodicidade = "Mensal" });

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("Desative 2");
    }

    [Fact]
    public async Task Detalhe_na_plataforma_nao_expoe_dados_de_clientes_finais()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId);
        using (var escopo = _fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            db.Clientes.Add(Cliente.Criar(negocioId, "Cliente Sigiloso", TelefoneE164.Criar("+5571988887777"), email: "sigiloso@cliente.com"));
            await db.SaveChangesAsync();
        }
        using var plataforma = await ClienteDaPlataformaAsync();

        var lista = await plataforma.GetStringAsync("/plataforma/negocios");
        var detalhe = await plataforma.GetStringAsync($"/plataforma/negocios/{negocioId}");

        foreach (var corpo in new[] { lista, detalhe })
            corpo.Should().NotContainAny("Cliente Sigiloso", "sigiloso@cliente.com", "5571988887777");
    }

    [Fact]
    public async Task Tela_de_assinatura_abre_para_o_administrador_mesmo_suspenso_e_nao_para_outros_perfis()
    {
        var (administrador, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, diasDesdeInicioDoTeste: 45);

        var resposta = await administrador.GetAsync("/painel/assinatura");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("Suspensa");

        var (recepcionista, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Recepcionista);
        (await recepcionista.GetAsync("/painel/assinatura")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Aviso_do_painel_aparece_nos_ultimos_7_dias_do_teste()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Recepcionista);
        await DarAssinaturaAsync(negocioId, diasDesdeInicioDoTeste: 25);

        var aviso = await cliente.GetFromJsonAsync<JsonElement>("/painel/assinatura/aviso");

        aviso.GetProperty("diasRestantes").GetInt32().Should().Be(5);
        aviso.GetProperty("destacado").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Sem_prazo_proximo_nao_ha_aviso()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, diasDesdeInicioDoTeste: 5);

        (await cliente.GetAsync("/painel/assinatura/aviso")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Instrucoes_de_pagamento_vem_da_configuracao()
    {
        using var fabrica = _fabrica.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Cobranca:ChavePix"] = "pix@exemplo.com", ["Cobranca:WhatsAppContato"] = "5571999990000" })));
        var (_, negocioId, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId);
        using var cliente = await LogarEmAsync(fabrica, email);

        var instrucoes = await cliente.GetFromJsonAsync<JsonElement>("/painel/assinatura/instrucoes-pagamento");

        instrucoes.GetProperty("chavePix").GetString().Should().Be("pix@exemplo.com");
        instrucoes.GetProperty("whatsAppContato").GetString().Should().Be("5571999990000");
        instrucoes.GetProperty("texto").GetString().Should().Contain("79,90").And.Contain("Ritmo");
    }

    [Fact]
    public async Task Negocio_nao_troca_para_plano_menor_com_profissionais_demais()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, plano: "Ritmo");
        await SemearProfissionaisAtivosAsync(negocioId, 4);

        var menor = await cliente.PostAsJsonAsync("/painel/assinatura/plano", new { planoId = await PlanoIdAsync("Começo"), periodicidade = "Anual" });
        var maior = await cliente.PostAsJsonAsync("/painel/assinatura/plano", new { planoId = await PlanoIdAsync("Casa Cheia"), periodicidade = "Anual" });

        menor.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await menor.Content.ReadAsStringAsync()).Should().Contain("\"excedente\":1");
        maior.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.GetFromJsonAsync<JsonElement>("/painel/assinatura")).GetProperty("precoMensalTravado").GetDecimal().Should().Be(88.90m);
    }

    private async Task SemearProfissionaisAtivosAsync(Guid negocioId, int quantidade)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        for (var i = 0; i < quantidade; i++)
            db.Profissionais.Add(Profissional.Criar(negocioId, $"Profissional {i}"));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> PlanoIdAsync(string nome)
    {
        using var escopo = _fabrica.Services.CreateScope();
        return await escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().Planos.Where(p => p.Nome == nome).Select(p => p.Id).SingleAsync();
    }

    private static async Task<HttpClient> LogarEmAsync(WebApplicationFactory<Program> fabrica, string email)
    {
        using var anonimo = fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new { email, senha = SemeadorDeUsuarios.SenhaPadrao });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }
}
