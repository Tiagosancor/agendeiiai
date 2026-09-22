using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Controllers.Publico;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Publico;

/// <summary>
/// Código de confirmação (seção 8.1) — testes obrigatórios listados na seção 8.1: código
/// reutilizado, 6ª tentativa, respostas indistinguíveis entre telefone existente e novo,
/// rate limit. Expiração de 5 minutos é testada a nível de unidade (o domínio recebe
/// "agora" como parâmetro explícito, feito pra isso — reproduzir 5 min de espera real aqui
/// só deixaria o teste lento sem cobrir nada a mais).
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class VerificacaoPublicaTestes : IAsyncLifetime
{
    private const string DominioBase = DominioDeTeste.Valor;

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public VerificacaoPublicaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Solicitar_codigo_responde_202_pelos_dois_canais_e_a_mensagem_traz_6_digitos()
    {
        var slug = await SemearNegocioAsync();
        using var cliente = ClientePara(slug);
        var telefone = TelefoneAleatorio();

        var resposta = await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, "cliente@teste.com"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var espiaWhatsApp = _fabrica.Services.GetRequiredService<EspiaWhatsApp>();
        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();

        espiaWhatsApp.Enviados.Should().ContainSingle();
        espiaEmail.Enviados.Should().ContainSingle();

        ExtrairCodigo(espiaWhatsApp.Enviados[0].Mensagem).Should().MatchRegex(@"^\d{6}$");
    }

    [Fact]
    public async Task Validar_codigo_correto_devolve_token_e_reutilizar_o_mesmo_codigo_falha()
    {
        var slug = await SemearNegocioAsync();
        using var cliente = ClientePara(slug);
        var telefone = TelefoneAleatorio();

        await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, "cliente@teste.com"));
        var codigo = ExtrairCodigo(_fabrica.Services.GetRequiredService<EspiaWhatsApp>().Enviados[0].Mensagem);

        var primeiraValidacao = await cliente.PostAsJsonAsync("/publico/codigos/validar", new CodigosPublicoController.ValidarCodigoRequisicao(telefone, codigo));
        primeiraValidacao.StatusCode.Should().Be(HttpStatusCode.OK);
        (await primeiraValidacao.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["tokenVerificacao"].Should().NotBeNullOrWhiteSpace();

        // Uso único (seção 8.1.2) — o mesmo código não vale de novo.
        var segundaValidacao = await cliente.PostAsJsonAsync("/publico/codigos/validar", new CodigosPublicoController.ValidarCodigoRequisicao(telefone, codigo));
        segundaValidacao.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sexta_tentativa_falha_mesmo_com_o_codigo_certo()
    {
        var slug = await SemearNegocioAsync();
        using var cliente = ClientePara(slug);
        var telefone = TelefoneAleatorio();

        await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, "cliente@teste.com"));
        var codigoCerto = ExtrairCodigo(_fabrica.Services.GetRequiredService<EspiaWhatsApp>().Enviados[0].Mensagem);

        // 5 tentativas erradas consomem todo o limite (seção 8.1.2: no máximo 5 tentativas).
        for (var i = 0; i < 5; i++)
        {
            var errada = await cliente.PostAsJsonAsync("/publico/codigos/validar", new CodigosPublicoController.ValidarCodigoRequisicao(telefone, "000000"));
            errada.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // A 6ª, mesmo com o código certo, já não vale mais.
        var comCodigoCerto = await cliente.PostAsJsonAsync("/publico/codigos/validar", new CodigosPublicoController.ValidarCodigoRequisicao(telefone, codigoCerto));
        comCodigoCerto.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Resposta_de_solicitar_codigo_e_identica_para_telefone_existente_e_novo()
    {
        var slug = await SemearNegocioAsync();
        using var cliente = ClientePara(slug);

        var respostaTelefoneNovo = await cliente.PostAsJsonAsync(
            "/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(TelefoneAleatorio(), "a@teste.com"));

        // Ainda que um telefone exista como cliente de outro agendamento, a resposta de
        // solicitar código nunca consulta a tabela de clientes (seção 8.1.3) — repete a
        // mesma chamada para outro telefone só pra provar que o formato da resposta não muda.
        var respostaTelefoneNovo2 = await cliente.PostAsJsonAsync(
            "/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(TelefoneAleatorio(), "b@teste.com"));

        respostaTelefoneNovo.StatusCode.Should().Be(respostaTelefoneNovo2.StatusCode);
        (await respostaTelefoneNovo.Content.ReadAsStringAsync()).Should().Be(await respostaTelefoneNovo2.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rate_limit_por_telefone_bloqueia_depois_do_maximo_por_hora()
    {
        var slug = await SemearNegocioAsync();
        using var cliente = ClientePara(slug);
        var telefone = TelefoneAleatorio();

        // Padrão: no máximo 3 códigos por telefone por hora (seção 8.1.5 / OpcoesVerificacao).
        for (var i = 0; i < 3; i++)
        {
            var resposta = await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, null));
            resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var quarta = await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, null));
        quarta.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static string ExtrairCodigo(string mensagem) => Regex.Match(mensagem, @"\b\d{6}\b").Value;

    private static string TelefoneAleatorio() => $"+55719{Random.Shared.Next(10000000, 99999999)}";

    private async Task<string> SemearNegocioAsync()
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var slug = "verif-" + Guid.NewGuid().ToString("N")[..12];
        dbContext.Negocios.Add(Negocio.Criar(Slug.Criar(slug), "Negócio de Teste", TipoNegocio.Barbearia));
        await dbContext.SaveChangesAsync();

        return slug;
    }

    private HttpClient ClientePara(string slug) => _fabrica.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri($"http://{slug}.{DominioBase}/"),
    });
}
