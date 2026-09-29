using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Infraestrutura.Assinaturas;
using Plataforma.Infraestrutura.Opcoes;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary><see cref="GatewayPagamentoAsaas"/> contra um Asaas simulado (<see cref="HttpMessageHandler"/>).</summary>
public sealed class GatewayPagamentoAsaasTestes
{
    private const string Token = "token-do-webhook";

    private static (GatewayPagamentoAsaas Gateway, AsaasSimulado Asaas) Criar()
    {
        var asaas = new AsaasSimulado();
        var http = new HttpClient(asaas) { BaseAddress = new Uri("https://api-sandbox.asaas.com/v3/") };
        var gateway = new GatewayPagamentoAsaas(
            http,
            Options.Create(new OpcoesAsaas { ChaveApi = "chave-secreta", TokenWebhook = Token, TimeoutSegundos = 5 }),
            Options.Create(new OpcoesMarca { NomeProduto = "Produto" }),
            NullLogger<GatewayPagamentoAsaas>.Instance);
        return (gateway, asaas);
    }

    private static RequisicaoWebhook Webhook(string corpo, string? token = Token) =>
        new(token is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["asaas-access-token"] = token }, corpo);

    [Fact]
    public async Task Contratar_cria_cliente_e_assinatura_com_fatura_a_escolher_e_cabecalhos_obrigatorios()
    {
        var (gateway, asaas) = Criar();
        asaas.Responder("POST", "customers", 200, """{"id":"cus_1"}""");
        asaas.Responder("POST", "subscriptions", 200, """{"id":"sub_1"}""");

        var cliente = await gateway.CriarClienteAsync(new DadosClienteGateway(Guid.NewGuid(), "Barbearia", "dono@x.com", null, "52998224725"));
        var assinatura = await gateway.CriarOuAlterarAssinaturaAsync(new DadosAssinaturaGateway(
            Guid.NewGuid(), cliente, "Ritmo", "Anual", 826.80m, PrimeiroVencimento: new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.Zero)));

        (cliente, assinatura).Should().Be(("cus_1", "sub_1"));
        var criacao = asaas.Recebidas.Single(r => r.Recurso == "subscriptions");
        using var corpo = JsonDocument.Parse(criacao.Corpo!);
        corpo.RootElement.GetProperty("billingType").GetString().Should().Be("UNDEFINED");
        corpo.RootElement.GetProperty("cycle").GetString().Should().Be("YEARLY");
        corpo.RootElement.GetProperty("value").GetDecimal().Should().Be(826.80m);
        // 02:00 UTC de 1º/10 ainda é 30/09 em Brasília.
        corpo.RootElement.GetProperty("nextDueDate").GetString().Should().Be("2026-09-30");
        asaas.Recebidas.Should().OnlyContain(r => r.ChaveApi == "chave-secreta" && r.UserAgent == "Produto");
    }

    [Fact]
    public async Task Troca_de_plano_altera_so_as_proximas_cobrancas()
    {
        var (gateway, asaas) = Criar();
        asaas.Responder("PUT", "subscriptions/sub_1", 200, """{"id":"sub_1"}""");

        await gateway.CriarOuAlterarAssinaturaAsync(new DadosAssinaturaGateway(Guid.NewGuid(), "cus_1", "Casa Cheia", "Mensal", 99.90m, "sub_1"));

        using var corpo = JsonDocument.Parse(asaas.Recebidas.Single().Corpo!);
        (corpo.RootElement.GetProperty("cycle").GetString(), corpo.RootElement.GetProperty("updatePendingPayments").GetBoolean())
            .Should().Be(("MONTHLY", false));
    }

    [Fact]
    public async Task Recusa_do_asaas_vira_falha_com_a_descricao_e_criar_nao_repete()
    {
        var (gateway, asaas) = Criar();
        asaas.Responder("POST", "customers", 400, """{"errors":[{"code":"invalid_cpfCnpj","description":"O CPF/CNPJ informado é inválido."}]}""");

        var criar = () => gateway.CriarClienteAsync(new DadosClienteGateway(Guid.NewGuid(), "B", "a@b.com", null, "00000000000"));

        (await criar.Should().ThrowAsync<FalhaGatewayPagamentoException>())
            .Which.Should().Match<FalhaGatewayPagamentoException>(e => e.Recusado && e.Message == "O CPF/CNPJ informado é inválido.");
        asaas.Recebidas.Should().ContainSingle();
    }

    [Fact]
    public async Task Fora_do_ar_repete_so_leitura_e_depois_falha_com_mensagem_generica()
    {
        var (gateway, asaas) = Criar();
        asaas.Responder("GET", "subscriptions/sub_1/payments", 503, "{}");
        asaas.Responder("POST", "subscriptions", 503, "{}");

        var ler = () => gateway.GerarLinkPagamentoAsync(new DadosCobrancaGateway(Guid.NewGuid(), "Começo", "Mensal", 49.90m, "sub_1"));
        (await ler.Should().ThrowAsync<FalhaGatewayPagamentoException>()).Which.Recusado.Should().BeFalse();
        asaas.Recebidas.Count(r => r.Metodo == "GET").Should().Be(3);

        var criar = () => gateway.CriarOuAlterarAssinaturaAsync(new DadosAssinaturaGateway(Guid.NewGuid(), "cus_1", "Começo", "Mensal", 49.90m));
        await criar.Should().ThrowAsync<FalhaGatewayPagamentoException>();
        asaas.Recebidas.Count(r => r.Metodo == "POST").Should().Be(1);
    }

    [Fact]
    public async Task Link_e_o_da_cobranca_em_aberto_mais_antiga()
    {
        var (gateway, asaas) = Criar();
        asaas.Responder("GET", "subscriptions/sub_1/payments", 200, """
            {"data":[
              {"id":"pay_3","value":49.9,"status":"RECEIVED","dueDate":"2026-08-01","invoiceUrl":"https://asaas/i/3"},
              {"id":"pay_5","value":49.9,"status":"PENDING","dueDate":"2026-10-01","invoiceUrl":"https://asaas/i/5"},
              {"id":"pay_4","value":49.9,"status":"OVERDUE","dueDate":"2026-09-01","invoiceUrl":"https://asaas/i/4"}]}
            """);

        var instrucoes = await gateway.GerarLinkPagamentoAsync(new DadosCobrancaGateway(Guid.NewGuid(), "Começo", "Mensal", 49.90m, "sub_1"));

        instrucoes.Link.Should().Be("https://asaas/i/4");
        instrucoes.Texto.Should().Contain("R$ 49,90").And.Contain("01/09/2026");
    }

    [Fact]
    public async Task Webhook_sem_o_token_certo_e_recusado()
    {
        var (gateway, asaas) = Criar();
        const string corpo = """{"id":"evt_1","event":"PAYMENT_RECEIVED","payment":{"id":"pay_1"}}""";

        (await gateway.InterpretarWebhookAsync(Webhook(corpo, token: null))).Should().BeNull();
        (await gateway.InterpretarWebhookAsync(Webhook(corpo, token: "outro"))).Should().BeNull();
        asaas.Recebidas.Should().BeEmpty();
    }

    [Fact]
    public async Task Pagamento_recebido_e_conferido_na_api_e_nao_no_corpo()
    {
        var (gateway, asaas) = Criar();
        asaas.Responder("GET", "payments/pay_1", 200, """
            {"id":"pay_1","subscription":"sub_1","value":49.9,"status":"RECEIVED","dueDate":"2026-10-01","paymentDate":"2026-09-30","billingType":"PIX"}
            """);

        // O corpo diz outro valor: vale o da API.
        var evento = await gateway.InterpretarWebhookAsync(Webhook(
            """{"id":"evt_1","event":"PAYMENT_RECEIVED","payment":{"id":"pay_1","value":1.0,"status":"RECEIVED"}}"""));

        evento!.Should().Match<EventoPagamentoGateway>(e =>
            e.Tipo == TipoEventoPagamento.PagamentoConfirmado && e.IdEvento == "evt_1" && e.IdExternoAssinatura == "sub_1"
            && e.IdExternoCobranca == "pay_1" && e.Valor == 49.9m && e.Forma == "Pix"
            && e.Vencimento == new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Evento_que_a_api_nao_confirma_ou_desconhecido_e_ignorado()
    {
        var (gateway, asaas) = Criar();
        asaas.Responder("GET", "payments/pay_1", 200, """{"id":"pay_1","value":49.9,"status":"PENDING"}""");

        (await gateway.InterpretarWebhookAsync(Webhook("""{"id":"evt_1","event":"PAYMENT_RECEIVED","payment":{"id":"pay_1"}}""")))!
            .Tipo.Should().Be(TipoEventoPagamento.Ignorado);
        (await gateway.InterpretarWebhookAsync(Webhook("""{"id":"evt_2","event":"PAYMENT_CHECKOUT_VIEWED","payment":{"id":"pay_1"}}""")))!
            .Should().Match<EventoPagamentoGateway>(e => e.Tipo == TipoEventoPagamento.Ignorado && e.Nome == "PAYMENT_CHECKOUT_VIEWED");
        (await gateway.InterpretarWebhookAsync(Webhook("não é json")))!.IdEvento.Should().StartWith("sem-id:");
    }

    [Theory]
    [InlineData("PAYMENT_OVERDUE", "OVERDUE", TipoEventoPagamento.CobrancaVencida)]
    [InlineData("PAYMENT_REFUNDED", "REFUNDED", TipoEventoPagamento.PagamentoEstornado)]
    [InlineData("PAYMENT_CHARGEBACK_REQUESTED", "CHARGEBACK_REQUESTED", TipoEventoPagamento.PagamentoEstornado)]
    public async Task Vencida_e_estorno_viram_eventos_neutros(string nome, string status, TipoEventoPagamento tipo)
    {
        var (gateway, asaas) = Criar();
        asaas.Responder("GET", "payments/pay_1", 200, $$"""{"id":"pay_1","subscription":"sub_1","value":49.9,"status":"{{status}}","dueDate":"2026-10-01"}""");

        var evento = await gateway.InterpretarWebhookAsync(Webhook($$$"""{"id":"evt_1","event":"{{{nome}}}","payment":{"id":"pay_1"}}"""));

        (evento!.Tipo, evento.IdExternoAssinatura, evento.IdExternoCobranca).Should().Be((tipo, "sub_1", "pay_1"));
    }

    [Fact]
    public async Task Assinatura_removida_e_conferida_na_api()
    {
        var (gateway, asaas) = Criar();
        asaas.Responder("GET", "subscriptions/sub_1", 200, """{"id":"sub_1","status":"ACTIVE","deleted":true}""");
        asaas.Responder("GET", "subscriptions/sub_2", 200, """{"id":"sub_2","status":"ACTIVE","deleted":false}""");

        (await gateway.InterpretarWebhookAsync(Webhook("""{"id":"evt_1","event":"SUBSCRIPTION_DELETED","subscription":{"id":"sub_1"}}""")))!
            .Tipo.Should().Be(TipoEventoPagamento.AssinaturaCancelada);
        (await gateway.InterpretarWebhookAsync(Webhook("""{"id":"evt_2","event":"SUBSCRIPTION_DELETED","subscription":{"id":"sub_2"}}""")))!
            .Tipo.Should().Be(TipoEventoPagamento.Ignorado);
    }

    /// <summary>Asaas de mentira: respostas por "MÉTODO recurso" e registro do que chegou (sem guardar nada fora do teste).</summary>
    private sealed class AsaasSimulado : HttpMessageHandler
    {
        private readonly Dictionary<string, (int Status, string Corpo)> _respostas = [];

        public List<(string Metodo, string Recurso, string? Corpo, string? ChaveApi, string? UserAgent)> Recebidas { get; } = [];

        public void Responder(string metodo, string recurso, int status, string corpo) => _respostas[$"{metodo} {recurso}"] = (status, corpo);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recurso = request.RequestUri!.AbsolutePath.Replace("/v3/", string.Empty);
            var corpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Recebidas.Add((request.Method.Method, recurso, corpo,
                request.Headers.TryGetValues("access_token", out var chave) ? chave.Single() : null,
                string.Join(' ', request.Headers.UserAgent.Select(u => u.ToString()))));

            var (status, resposta) = _respostas.GetValueOrDefault($"{request.Method.Method} {recurso}", (404, "{}"));
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(resposta, Encoding.UTF8, "application/json") };
        }
    }
}
