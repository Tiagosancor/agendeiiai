using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Notificacoes;
using Plataforma.Infraestrutura.Opcoes;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

public sealed class MensageriaWhatsAppEvolutionTestes
{
    private static readonly TelefoneE164 Telefone = TelefoneE164.Criar("+5571988887777");

    private static MensageriaWhatsAppEvolution Provedor(InstanciaFalsa instancia, int timeoutSegundos = 5) => new(
        new HttpClient(instancia),
        Options.Create(new OpcoesWhatsApp
        {
            Evolution = new OpcoesEvolutionApi
            {
                UrlBase = "http://evolution.teste/",
                ChaveApi = "chave-123",
                NomeInstancia = "minha instancia",
                SegredoWebhook = "segredo-0123456789",
                TimeoutSegundos = timeoutSegundos,
            },
        }),
        NullLogger<MensageriaWhatsAppEvolution>.Instance);

    [Fact]
    public async Task Envia_para_a_instancia_com_a_chave_e_devolve_o_id_aguardando_confirmacao()
    {
        var instancia = InstanciaFalsa.Sincrona(() => Json(HttpStatusCode.Created, """{ "key": { "id": "BAE5ABC" }, "status": "PENDING" }"""));

        var resultado = await Provedor(instancia).EnviarAsync(Telefone, "Seu código: 123456");

        resultado.Should().Be(new ResultadoEnvioWhatsApp(StatusCanal.Pendente, "BAE5ABC"));
        var requisicao = instancia.Recebidas.Single();
        requisicao.Url.Should().Be("http://evolution.teste/message/sendText/minha%20instancia");
        requisicao.ChaveApi.Should().Be("chave-123");
        requisicao.Corpo.Should().Contain("\"number\":\"5571988887777\"").And.Contain("Seu c");
    }

    [Fact]
    public async Task Falha_de_rede_tenta_de_novo_uma_unica_vez_e_devolve_indisponivel_sem_lancar()
    {
        var instancia = InstanciaFalsa.Sincrona(() => throw new HttpRequestException("Connection refused"));

        var resultado = await Provedor(instancia).EnviarAsync(Telefone, "oi");

        resultado.Status.Should().Be(StatusCanal.Falhou);
        instancia.Recebidas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Instancia_que_nao_responde_estoura_o_timeout_sem_nova_tentativa()
    {
        var instancia = new InstanciaFalsa(async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            return Json(HttpStatusCode.OK, "{}");
        });

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var resultado = await Provedor(instancia, timeoutSegundos: 1).EnviarAsync(Telefone, "oi");

        resultado.Status.Should().Be(StatusCanal.Falhou);
        // Repetir só dobraria a espera do cliente: com a sessão caída, a instância segura a requisição.
        instancia.Recebidas.Should().ContainSingle();
        cronometro.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Erro_5xx_e_repetido_uma_vez_e_o_retry_pode_dar_certo()
    {
        var chamadas = 0;
        var instancia = InstanciaFalsa.Sincrona(() => ++chamadas == 1
            ? Json(HttpStatusCode.ServiceUnavailable, "{}")
            : Json(HttpStatusCode.Created, """{ "key": { "id": "SEGUNDA" } }"""));

        var resultado = await Provedor(instancia).EnviarAsync(Telefone, "oi");

        resultado.Should().Be(new ResultadoEnvioWhatsApp(StatusCanal.Pendente, "SEGUNDA"));
    }

    [Fact]
    public async Task Recusa_da_instancia_4xx_nao_e_repetida()
    {
        var instancia = InstanciaFalsa.Sincrona(() => Json(HttpStatusCode.BadRequest, """{ "message": "number not exists" }"""));

        var resultado = await Provedor(instancia).EnviarAsync(Telefone, "oi");

        resultado.Status.Should().Be(StatusCanal.Falhou);
        instancia.Recebidas.Should().ContainSingle();
    }

    [Theory]
    [InlineData("open", EstadoConexaoWhatsApp.Conectada)]
    [InlineData("connecting", EstadoConexaoWhatsApp.AguardandoQrCode)]
    [InlineData("close", EstadoConexaoWhatsApp.Desconectada)]
    public async Task Estado_da_conexao_vem_do_connectionState_da_instancia(string estado, EstadoConexaoWhatsApp esperado)
    {
        var instancia = InstanciaFalsa.Sincrona(() => Json(HttpStatusCode.OK, $$"""{ "instance": { "instanceName": "x", "state": "{{estado}}" } }"""));

        var (resultado, _) = await Provedor(instancia).ConsultarConexaoAsync();

        resultado.Should().Be(esperado);
        instancia.Recebidas.Single().Url.Should().Be("http://evolution.teste/instance/connectionState/minha%20instancia");
    }

    [Fact]
    public async Task Instancia_fora_do_ar_aparece_como_indisponivel_na_saude()
    {
        var instancia = InstanciaFalsa.Sincrona(() => throw new HttpRequestException("Connection refused"));

        var (resultado, detalhe) = await Provedor(instancia).ConsultarConexaoAsync();

        resultado.Should().Be(EstadoConexaoWhatsApp.Indisponivel);
        detalhe.Should().NotBeNullOrWhiteSpace();
    }

    // ---------------------------------------------------------------- webhook: leitura do corpo

    [Fact]
    public void Webhook_le_o_formato_v2_objeto_e_lista()
    {
        ProcessadorWebhookWhatsApp.LerEventos("""{ "event": "messages.update", "data": { "keyId": "A1", "status": "DELIVERY_ACK", "fromMe": true } }""")
            .Should().Equal(("A1", StatusCanal.Enviado));

        ProcessadorWebhookWhatsApp.LerEventos("""{ "event": "MESSAGES_UPDATE", "data": [ { "keyId": "A2", "status": "ERROR" }, { "keyId": "A3", "status": "READ" } ] }""")
            .Should().Equal(("A2", StatusCanal.Falhou), ("A3", StatusCanal.Enviado));
    }

    [Fact]
    public void Webhook_le_o_formato_antigo_com_status_numerico()
    {
        ProcessadorWebhookWhatsApp.LerEventos("""{ "event": "messages.update", "data": { "key": { "id": "B1", "fromMe": true }, "update": { "status": 3 } } }""")
            .Should().Equal(("B1", StatusCanal.Enviado));
    }

    [Theory]
    [InlineData("""{ "event": "connection.update", "data": { "state": "open" } }""")]
    [InlineData("""{ "event": "messages.update", "data": { "keyId": "C1", "status": "PENDING" } }""")]
    [InlineData("""{ "event": "messages.update", "data": { "keyId": "C2", "status": "READ", "fromMe": false } }""")]
    [InlineData("""{ "event": "messages.update", "data": { "status": "READ" } }""")]
    public void Webhook_ignora_o_que_nao_e_status_de_mensagem_nossa(string corpo) =>
        ProcessadorWebhookWhatsApp.LerEventos(corpo).Should().BeEmpty();

    // ---------------------------------------------------------------- flag de provedor não oficial

    [Fact]
    public void Evolution_sem_a_flag_de_nao_oficial_nao_sobe()
    {
        var opcoes = new OpcoesWhatsApp
        {
            Provedor = ProvedorWhatsApp.EvolutionApi,
            PermitirNaoOficial = false,
            Evolution = new OpcoesEvolutionApi { UrlBase = "http://e", ChaveApi = "c", NomeInstancia = "i", SegredoWebhook = "segredo-0123456789" },
        };

        opcoes.Validate(new ValidationContext(opcoes)).Should().ContainSingle(r => r.ErrorMessage!.Contains("PermitirNaoOficial"));

        opcoes.PermitirNaoOficial = true;
        opcoes.Validate(new ValidationContext(opcoes)).Should().BeEmpty();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string corpo) =>
        new(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };

    private sealed class InstanciaFalsa : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _responder;

        public InstanciaFalsa(Func<CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;

        /// <summary>Resposta imediata (ou exceção de rede, lançada dentro do SendAsync, como o HttpClient real).</summary>
        public static InstanciaFalsa Sincrona(Func<HttpResponseMessage> responder) => new(_ => Task.FromResult(responder()));

        public List<(string Url, string? ChaveApi, string Corpo)> Recebidas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Recebidas.Add((request.RequestUri!.AbsoluteUri, request.Headers.TryGetValues("apikey", out var chave) ? chave.Single() : null, corpo));
            return await _responder(cancellationToken);
        }
    }
}
