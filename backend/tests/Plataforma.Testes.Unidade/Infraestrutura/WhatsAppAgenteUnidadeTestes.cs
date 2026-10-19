using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

public sealed class WhatsAppAgenteUnidadeTestes
{
    [Theory]
    [InlineData("segredo-certo-0123456789", true)]
    [InlineData("segredo-errado-0123456789", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Segredo_so_confere_quando_igual(string? recebido, bool esperado) =>
        ProcessadorWebhookAgente.SegredoConfere(recebido, "segredo-certo-0123456789").Should().Be(esperado);

    [Fact]
    public async Task Redis_nao_configurado_deixa_passar()
    {
        var deduplicador = new DeduplicadorMensagensRedis(Options.Create(new OpcoesRedis()), NullLogger<DeduplicadorMensagensRedis>.Instance);

        (await deduplicador.RegistrarSeNovaAsync("i", "m")).Should().BeTrue();
    }

    [Fact]
    public async Task Redis_fora_do_ar_deixa_passar_e_nao_trava_as_proximas_chamadas()
    {
        // Porta 1: ninguém escuta. O webhook segue e o índice único do banco barra a duplicata.
        var deduplicador = new DeduplicadorMensagensRedis(
            Options.Create(new OpcoesRedis { ConnectionString = "127.0.0.1:1,abortConnect=false" }),
            NullLogger<DeduplicadorMensagensRedis>.Instance);

        (await deduplicador.RegistrarSeNovaAsync("i", "m")).Should().BeTrue();

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        (await deduplicador.RegistrarSeNovaAsync("i", "m")).Should().BeTrue();
        cronometro.ElapsedMilliseconds.Should().BeLessThan(200, "depois da falha o Redis é pulado por um tempo");
    }

    [Fact]
    public async Task Cliente_Evolution_manda_apikey_e_o_corpo_do_sendText()
    {
        var manipulador = new ManipuladorFalso(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"key":{"id":"ABC123"}}""", Encoding.UTF8, "application/json"),
        });
        var cliente = CriarCliente(manipulador);

        var id = await cliente.EnviarTextoAsync("neg-1", "+5571988887777", "ola");

        id.Should().Be("ABC123");
        manipulador.Requisicoes.Should().ContainSingle();
        manipulador.Requisicoes[0].Uri.Should().Be("http://evolution.invalid/message/sendText/neg-1");
        manipulador.Requisicoes[0].ApiKey.Should().Be("chave-global");
        manipulador.Requisicoes[0].Corpo.Should().Contain("\"number\":\"5571988887777\"").And.Contain("\"text\":\"ola\"");
    }

    [Fact]
    public async Task Cliente_Evolution_cria_instancia_com_webhook_por_instancia_e_le_o_qr_code()
    {
        var manipulador = new ManipuladorFalso(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"instance":{"instanceName":"neg-1"},"hash":"x","qrcode":{"base64":"data:image/png;base64,QQ"}}""", Encoding.UTF8, "application/json"),
        });
        var cliente = CriarCliente(manipulador);

        var qr = await cliente.CriarInstanciaAsync("neg-1", "http://api/webhooks/whatsapp/segredo");

        qr.Should().Be("data:image/png;base64,QQ");
        var corpo = manipulador.Requisicoes[0].Corpo;
        corpo.Should().Contain("\"instanceName\":\"neg-1\"").And.Contain("\"url\":\"http://api/webhooks/whatsapp/segredo\"")
            .And.Contain("MESSAGES_UPSERT").And.Contain("QRCODE_UPDATED").And.Contain("CONNECTION_UPDATE")
            .And.NotContain("MESSAGES_UPDATE");
    }

    [Theory]
    [InlineData("""{"instance":{"state":"open"}}""", EstadoInstanciaEvolution.Aberta)]
    [InlineData("""{"instance":{"state":"connecting"}}""", EstadoInstanciaEvolution.Conectando)]
    [InlineData("""{"instance":{"state":"close"}}""", EstadoInstanciaEvolution.Fechada)]
    public async Task Cliente_Evolution_interpreta_o_status(string json, EstadoInstanciaEvolution esperado)
    {
        var cliente = CriarCliente(new ManipuladorFalso(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));

        (await cliente.ConsultarStatusAsync("neg-1")).Should().Be(esperado);
    }

    [Fact]
    public async Task Cliente_Evolution_404_no_status_e_instancia_inexistente_e_5xx_vira_falha()
    {
        var naoExiste = CriarCliente(new ManipuladorFalso(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        (await naoExiste.ConsultarStatusAsync("neg-1")).Should().Be(EstadoInstanciaEvolution.NaoExiste);

        var foraDoAr = CriarCliente(new ManipuladorFalso(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));
        await foraDoAr.Invoking(c => c.EnviarTextoAsync("neg-1", "+5571988887777", "oi")).Should().ThrowAsync<FalhaEvolutionException>();
    }

    private static ClienteEvolution CriarCliente(ManipuladorFalso manipulador) => new(
        new HttpClient(manipulador),
        Options.Create(new OpcoesEvolution { BaseUrl = "http://evolution.invalid", ApiKey = "chave-global" }),
        NullLogger<ClienteEvolution>.Instance);

    private sealed class ManipuladorFalso(Func<HttpRequestMessage, HttpResponseMessage> resposta) : HttpMessageHandler
    {
        public List<(string Uri, string? ApiKey, string Corpo)> Requisicoes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requisicao, CancellationToken cancellationToken)
        {
            var corpo = requisicao.Content is null ? string.Empty : await requisicao.Content.ReadAsStringAsync(cancellationToken);
            Requisicoes.Add((requisicao.RequestUri!.ToString(), requisicao.Headers.TryGetValues("apikey", out var v) ? v.Single() : null, corpo));
            return resposta(requisicao);
        }
    }
}
