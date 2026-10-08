using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Negocios;

/// <summary>
/// D7.2 (H1): rota pública que depende do negócio, chamada sem negócio nenhum, responde 404 — o mesmo do slug que não
/// existe — em vez de 500. As rotas que o frontend chama justamente para descobrir o negócio continuam sem exigi-lo.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class RotasPublicasSemNegocioTestes : IAsyncLifetime
{
    private const string DominioBase = DominioDeTeste.Valor;
    private const string TokenSintetico = "token.invalido.sintetico";
    private const string Guid0 = "00000000-0000-0000-0000-000000000001";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public RotasPublicasSemNegocioTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();

        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        db.Negocios.Add(Negocio.Criar(Slug.Criar("acme"), "Acme Barbearia", TipoNegocio.Barbearia));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    public static IEnumerable<object[]> RotasQueDependemDoNegocio() =>
    [
        ["GET", "/publico/negocio", ""],
        ["GET", "/publico/servicos", ""],
        ["GET", "/publico/profissionais", ""],
        ["GET", "/publico/horarios-livres?data=2030-01-07&duracaoMinutos=30", ""],
        ["GET", $"/publico/meus-agendamentos/{TokenSintetico}", ""],
        ["GET", $"/publico/meus-agendamentos/{TokenSintetico}/horarios-livres?data=2030-01-07", ""],
        ["GET", $"/publico/meus-agendamentos/{TokenSintetico}/ics", ""],
        ["POST", $"/publico/meus-agendamentos/{TokenSintetico}/cancelar", ""],
        ["POST", $"/publico/meus-agendamentos/{TokenSintetico}/remarcar", "{\"novoInicio\":\"2030-01-07T13:00:00Z\"}"],
        ["POST", "/publico/reservas", $"{{\"profissionalId\":\"{Guid0}\",\"servicoIds\":[\"{Guid0}\"],\"inicio\":\"2030-01-07T13:00:00Z\"}}"],
        ["POST", "/publico/cupons/validar", $"{{\"agendamentoId\":\"{Guid0}\",\"codigo\":\"X\"}}"],
        ["POST", "/publico/agendamentos", $"{{\"agendamentoId\":\"{Guid0}\",\"tokenVerificacao\":\"x\",\"nome\":\"A\",\"telefone\":\"+5571999990000\"}}"],
        ["POST", "/publico/codigos", "{\"telefone\":\"+5571999990000\",\"email\":null}"],
        ["POST", "/publico/codigos/validar", "{\"telefone\":\"+5571999990000\",\"codigo\":\"000000\"}"],
        ["POST", "/publico/codigos/reenviar", "{\"telefone\":\"+5571999990000\"}"],
        ["POST", "/publico/contato", "{\"nome\":\"A\",\"telefone\":\"+5571999990000\",\"email\":\"a@a.com\",\"mensagem\":\"oi\"}"],
    ];

    [Theory]
    [MemberData(nameof(RotasQueDependemDoNegocio))]
    public async Task Sem_negocio_a_rota_publica_responde_404_igual_ao_slug_inexistente(string metodo, string rota, string corpo)
    {
        using var semNegocio = ClienteDaApi();                       // api.{dominio}, sem cabeçalho: nenhum negócio
        using var slugInexistente = ClientePara("nao-existe");       // negócio inexistente
        using var dominioBase = ClienteDoDominioBase();              // {dominio} puro: também nenhum negócio

        var referencia = await EnviarAsync(slugInexistente, metodo, rota, corpo);
        referencia.StatusCode.Should().Be(HttpStatusCode.NotFound);

        foreach (var cliente in new[] { semNegocio, dominioBase })
        {
            var resposta = await EnviarAsync(cliente, metodo, rota, corpo);

            resposta.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{metodo} {rota}");
            // Ausência e inexistência não se distinguem: mesmo corpo.
            (await resposta.Content.ReadAsStringAsync()).Should().Be(await referencia.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Gestao_por_token_sem_negocio_ou_com_negocio_inexistente_nunca_fica_em_cache()
    {
        using var semNegocio = ClienteDaApi();
        using var slugInexistente = ClientePara("nao-existe");

        foreach (var cliente in new[] { semNegocio, slugInexistente })
        {
            foreach (var rota in new[] { "", "/horarios-livres?data=2030-01-07", "/ics" })
            {
                var resposta = await cliente.GetAsync($"/publico/meus-agendamentos/{TokenSintetico}{rota}");
                resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
                resposta.Headers.CacheControl!.NoStore.Should().BeTrue($"GET {rota}");
            }

            (await cliente.PostAsync($"/publico/meus-agendamentos/{TokenSintetico}/cancelar", null))
                .Headers.CacheControl!.NoStore.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Com_negocio_resolvido_token_invalido_continua_401_e_nao_vira_404()
    {
        using var acme = ClientePara("acme");

        foreach (var rota in new[] { "", "/horarios-livres?data=2030-01-07", "/ics" })
        {
            var resposta = await acme.GetAsync($"/publico/meus-agendamentos/{TokenSintetico}{rota}");
            resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("link_invalido");
        }

        (await acme.GetAsync("/publico/servicos")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await acme.GetAsync("/publico/negocio")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rotas_que_o_frontend_usa_para_descobrir_o_negocio_continuam_funcionando_sem_negocio()
    {
        using var api = ClienteDaApi();

        (await api.GetAsync("/publico/negocios-por-slug/acme")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await api.GetAsync("/publico/negocios-por-slug/nao-existe")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Sem link antigo registrado, o 404 vem do próprio endpoint (não do filtro de negócio ausente).
        var antigo = await api.GetAsync("/publico/slugs-anteriores/nunca-existiu");
        antigo.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await antigo.Content.ReadAsStringAsync()).Should().NotContain("Negócio não encontrado");
    }

    [Fact]
    public async Task Rotas_fora_de_publico_nao_exigem_negocio()
    {
        using var api = ClienteDaApi();
        using var dominioBase = ClienteDoDominioBase();

        foreach (var cliente in new[] { api, dominioBase })
        {
            (await cliente.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await cliente.GetAsync("/cadastro/planos")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string metodo, string rota, string corpo)
    {
        using var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota);
        if (corpo.Length > 0)
            requisicao.Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json");
        return await cliente.SendAsync(requisicao);
    }

    private HttpClient ClientePara(string slug) => _fabrica.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri($"http://{slug}.{DominioBase}/"),
    });

    private HttpClient ClienteDaApi() => ClientePara("api");

    private HttpClient ClienteDoDominioBase() => _fabrica.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri($"http://{DominioBase}/"),
    });
}
