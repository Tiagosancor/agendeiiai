using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Negocios;

/// <summary>
/// Sprint 0 (seção 11): "acme.localhost resolve o negócio acme e slug inexistente dá 404";
/// "teste de isolamento entre negócios passa" (seção 8.3.1).
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ResolucaoNegocioTestes : IAsyncLifetime
{
    private const string DominioBase = DominioDeTeste.Valor;

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public ResolucaoNegocioTestes(PostgresContainerFixture postgres)
    {
        _postgres = postgres;
    }

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);

        // Força o host a subir (e as migrations a rodar) antes de resetar o banco.
        using var clienteDeAquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync()
    {
        await _fabrica.DisposeAsync();
    }

    [Fact]
    public async Task Subdominio_de_negocio_ativo_resolve_o_negocio_correto()
    {
        await SemearNegocioAsync("acme", "Acme Barbearia", TipoNegocio.Barbearia);

        using var cliente = ClientePara("acme");
        var resposta = await cliente.GetAsync("/publico/negocio");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var negocio = await resposta.Content.ReadFromJsonAsync<NegocioResumo>();
        negocio!.Slug.Should().Be("acme");
        negocio.NomeExibido.Should().Be("Acme Barbearia");
    }

    [Fact]
    public async Task Subdominio_sem_negocio_cadastrado_da_404()
    {
        using var cliente = ClientePara("nao-existe");
        var resposta = await cliente.GetAsync("/publico/negocio");

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Subdominio_reservado_da_404_mesmo_que_alguem_tente_cadastrar()
    {
        using var cliente = ClientePara("admin");
        var resposta = await cliente.GetAsync("/publico/negocio");

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Um_negocio_nunca_ve_dados_de_outro_negocio()
    {
        await SemearNegocioAsync("acme", "Acme Barbearia", TipoNegocio.Barbearia);
        await SemearNegocioAsync("beta", "Beta Salão", TipoNegocio.Salao);

        using var clienteAcme = ClientePara("acme");
        var negocioAcme = await (await clienteAcme.GetAsync("/publico/negocio"))
            .Content.ReadFromJsonAsync<NegocioResumo>();

        using var clienteBeta = ClientePara("beta");
        var negocioBeta = await (await clienteBeta.GetAsync("/publico/negocio"))
            .Content.ReadFromJsonAsync<NegocioResumo>();

        negocioAcme!.Slug.Should().Be("acme");
        negocioAcme.NomeExibido.Should().NotBe(negocioBeta!.NomeExibido);
        negocioBeta.Slug.Should().Be("beta");
    }

    [Fact]
    public async Task Resolucao_por_slug_explicito_usada_pelo_middleware_do_frontend_funciona()
    {
        await SemearNegocioAsync("acme", "Acme Barbearia", TipoNegocio.Barbearia);

        using var cliente = _fabrica.CreateClient();

        (await cliente.GetAsync("/publico/negocios-por-slug/acme")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        (await cliente.GetAsync("/publico/negocios-por-slug/nao-existe")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task API_publicada_em_subdominio_reservado_atende_as_rotas_publicas()
    {
        // Produção: a API fica em api.{dominio}. "api" é reservado, então não é negócio — o
        // tenant vem do cabeçalho que o proxy do frontend manda (ou do slug na rota).
        await SemearNegocioAsync("acme", "Acme Barbearia", TipoNegocio.Barbearia);

        using var api = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://api.{DominioBase}/") });

        (await api.GetAsync("/publico/negocios-por-slug/acme")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await api.GetAsync("/publico/negocios-por-slug/nao-existe")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var comCabecalho = new HttpRequestMessage(HttpMethod.Get, "/publico/negocio");
        comCabecalho.Headers.Add("X-Slug-Negocio", "acme");
        var resposta = await api.SendAsync(comCabecalho);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<NegocioResumo>())!.Slug.Should().Be("acme");

        // Sem o cabeçalho, não há negócio nenhum — e nunca o "api" como se fosse um.
        (await api.GetAsync("/publico/negocio")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cabecalho_nunca_sobrepoe_o_negocio_do_host()
    {
        await SemearNegocioAsync("acme", "Acme Barbearia", TipoNegocio.Barbearia);
        await SemearNegocioAsync("beta", "Beta Salão", TipoNegocio.Salao);

        using var cliente = ClientePara("acme");
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/publico/negocio");
        requisicao.Headers.Add("X-Slug-Negocio", "beta");

        var negocio = await (await cliente.SendAsync(requisicao)).Content.ReadFromJsonAsync<NegocioResumo>();

        negocio!.Slug.Should().Be("acme");
    }

    [Fact]
    public async Task Host_do_painel_ou_do_dominio_base_nao_exige_negocio()
    {
        using var clienteDominioBase = _fabrica.CreateClient();
        clienteDominioBase.BaseAddress = new Uri($"http://{DominioBase}/");
        (await clienteDominioBase.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var clientePainel = _fabrica.CreateClient();
        clientePainel.BaseAddress = new Uri($"http://app.{DominioBase}/");
        (await clientePainel.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task SemearNegocioAsync(string slug, string nomeExibido, TipoNegocio tipo)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        dbContext.Negocios.Add(Negocio.Criar(Slug.Criar(slug), nomeExibido, tipo));
        await dbContext.SaveChangesAsync();
    }

    private HttpClient ClientePara(string slug)
    {
        var cliente = _fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{slug}.{DominioBase}/"),
        });

        return cliente;
    }
}
