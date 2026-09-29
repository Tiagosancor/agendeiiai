using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Assinaturas;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Assinaturas;

/// <summary>
/// Cobrança da assinatura pelo Asaas, ponta a ponta contra um Asaas simulado: contratar (CPF só mascarado aqui), webhook com
/// token, idempotência (evento repetido e "confirmado" + "recebido" da mesma cobrança), vencida, estorno e cancelamento.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class CobrancaAsaasTestes : IAsyncLifetime
{
    private const string Token = "token-webhook-teste";
    private const string Webhook = "/webhooks/pagamentos/asaas";

    private static readonly JsonSerializerOptions JsonApi = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly PostgresContainerFixture _postgres;
    private readonly AsaasSimulado _asaas = new();
    private PlataformaWebApplicationFactory _fabrica = null!;

    public CobrancaAsaasTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        var configuracao = new Dictionary<string, string?>
        {
            ["Cobranca:Provedor"] = "Asaas",
            ["Asaas:ChaveApi"] = "chave-teste",
            ["Asaas:TokenWebhook"] = Token,
        };
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, configuracao, servicos =>
            servicos.AddScoped(sp => new GatewayPagamentoAsaas(
                new HttpClient(_asaas, disposeHandler: false) { BaseAddress = new Uri("https://api-sandbox.asaas.com/v3/") },
                sp.GetRequiredService<IOptions<OpcoesAsaas>>(), sp.GetRequiredService<IOptions<OpcoesMarca>>(),
                NullLogger<GatewayPagamentoAsaas>.Instance)));
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Contratar_no_teste_cria_cliente_e_assinatura_para_o_fim_do_teste_e_guarda_so_o_cpf_mascarado()
    {
        var (admin, negocioId) = await NegocioEmTesteAsync();
        _asaas.Responder("POST", "customers", 200, """{"id":"cus_1"}""");
        _asaas.Responder("POST", "subscriptions", 200, """{"id":"sub_1"}""");
        _asaas.Responder("GET", "subscriptions/sub_1/payments", 200,
            """{"data":[{"id":"pay_1","value":49.9,"status":"PENDING","dueDate":"2026-10-29","invoiceUrl":"https://sandbox.asaas.com/i/1"}]}""");

        var resposta = await admin.PostAsJsonAsync("/painel/assinatura/contratar",
            new AssinaturaController.ContratarRequisicao(await PlanoAsync("Começo"), Periodicidade.Mensal, "529.982.247-25"));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<InstrucoesPagamento>())!.Link.Should().Be("https://sandbox.asaas.com/i/1");

        var assinatura = await AssinaturaAsync(negocioId);
        (assinatura.ProvedorGateway, assinatura.IdExternoGateway, assinatura.IdClienteGateway, assinatura.DocumentoTitularMascarado)
            .Should().Be(("asaas", "sub_1", "cus_1", "***.982.247-**"));
        assinatura.Estado.Should().Be(EstadoAssinatura.EmTeste);

        using var criacao = JsonDocument.Parse(_asaas.Recebidas.Single(r => r.Recurso == "subscriptions").Corpo!);
        criacao.RootElement.GetProperty("nextDueDate").GetString().Should()
            .Be(assinatura.FimTeste!.Value.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd"));
        criacao.RootElement.GetProperty("billingType").GetString().Should().Be("UNDEFINED");

        // O CPF completo só foi para o Asaas: nenhuma coluna do banco o tem.
        (await _fabrica.NoBancoAsync(db => db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM assinaturas WHERE documento_titular_mascarado LIKE '%52998224725%'").SingleAsync())).Should().Be(0);

        var detalhe = (await admin.GetFromJsonAsync<DetalheAssinaturaNegocio>("/painel/assinatura", JsonApi))!;
        (detalhe.PagamentoAutomatico, detalhe.Contratada, detalhe.DocumentoTitular).Should().Be((true, true, "***.982.247-**"));
    }

    [Fact]
    public async Task Documento_invalido_e_recusa_do_asaas_voltam_400_e_asaas_fora_do_ar_502()
    {
        var (admin, _) = await NegocioEmTesteAsync();
        var plano = await PlanoAsync("Começo");

        (await admin.PostAsJsonAsync("/painel/assinatura/contratar", new AssinaturaController.ContratarRequisicao(plano, Periodicidade.Mensal, "123")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        _asaas.Responder("POST", "customers", 400, """{"errors":[{"code":"invalid_email","description":"O e-mail informado é inválido."}]}""");
        var recusa = await admin.PostAsJsonAsync("/painel/assinatura/contratar", new AssinaturaController.ContratarRequisicao(plano, Periodicidade.Mensal, "52998224725"));
        recusa.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await recusa.Content.ReadAsStringAsync()).Should().Contain("O e-mail informado é inválido.");

        _asaas.Responder("POST", "customers", 503, "{}");
        (await admin.PostAsJsonAsync("/painel/assinatura/contratar", new AssinaturaController.ContratarRequisicao(plano, Periodicidade.Mensal, "52998224725")))
            .StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Webhook_sem_token_e_401_e_pagamento_confirmado_e_recebido_ativa_uma_vez_so()
    {
        var (_, negocioId) = await NegocioContratadoAsync("sub_A");
        _asaas.Responder("GET", "payments/pay_A1", 200, """
            {"id":"pay_A1","subscription":"sub_A","value":49.9,"status":"RECEIVED","dueDate":"2026-10-29","paymentDate":"2026-10-28","billingType":"CREDIT_CARD"}
            """);
        using var cliente = _fabrica.CreateClient();

        (await cliente.SendAsync(Evento("evt_1", "PAYMENT_CONFIRMED", pagamento: "pay_A1", token: "errado"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        foreach (var (id, nome) in new[] { ("evt_1", "PAYMENT_CONFIRMED"), ("evt_1", "PAYMENT_CONFIRMED"), ("evt_2", "PAYMENT_RECEIVED") })
            (await cliente.SendAsync(Evento(id, nome, pagamento: "pay_A1"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var assinatura = await AssinaturaAsync(negocioId);
        assinatura.Estado.Should().Be(EstadoAssinatura.Ativa);
        // Vence no fim do dia (Brasília) do próximo vencimento: 29/11.
        assinatura.ProximoVencimento!.Value.ToOffset(TimeSpan.FromHours(-3)).Should().Be(new DateTimeOffset(2026, 11, 29, 23, 59, 59, TimeSpan.FromHours(-3)));

        var cobrancas = await _fabrica.NoBancoAsync(db => db.CobrancasAssinatura.IgnoreQueryFilters().Where(c => c.AssinaturaId == assinatura.Id).ToListAsync());
        cobrancas.Should().ContainSingle().Which.Should().Match<CobrancaAssinatura>(c =>
            c.IdExterno == "pay_A1" && c.Valor == 49.9m && c.Forma == FormaCobranca.Cartao && c.Origem == OrigemCobranca.Gateway);

        var eventos = await _fabrica.NoBancoAsync(db => db.EventosWebhookPagamento.OrderBy(e => e.CriadoEm).ToListAsync());
        eventos.Select(e => (e.IdEvento, e.Tipo, e.Resultado)).Should().Equal(
            ("evt_1", "PAYMENT_CONFIRMED", "Aplicado"), ("evt_2", "PAYMENT_RECEIVED", "JaRegistrado"));
        eventos[0].Corpo.Should().Contain("pay_A1");
    }

    [Fact]
    public async Task Vencida_leva_a_carencia_e_estorno_tira_o_periodo_pago()
    {
        var (_, negocioId) = await NegocioContratadoAsync("sub_B");
        using var cliente = _fabrica.CreateClient();

        _asaas.Responder("GET", "payments/pay_B1", 200, """{"id":"pay_B1","subscription":"sub_B","value":49.9,"status":"RECEIVED","dueDate":"2026-10-29","billingType":"PIX"}""");
        await cliente.SendAsync(Evento("evt_b1", "PAYMENT_RECEIVED", pagamento: "pay_B1"));
        (await AssinaturaAsync(negocioId)).Estado.Should().Be(EstadoAssinatura.Ativa);

        _asaas.Responder("GET", "payments/pay_B1", 200, """{"id":"pay_B1","subscription":"sub_B","value":49.9,"status":"REFUNDED","dueDate":"2026-10-29"}""");
        (await cliente.SendAsync(Evento("evt_b2", "PAYMENT_REFUNDED", pagamento: "pay_B1"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var estornada = await AssinaturaAsync(negocioId);
        estornada.Estado.Should().Be(EstadoAssinatura.Atrasada);
        estornada.CarenciaAte.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(Assinatura.DiasCarencia), TimeSpan.FromMinutes(1));
        (await _fabrica.NoBancoAsync(db => db.CobrancasAssinatura.IgnoreQueryFilters().SingleAsync(c => c.IdExterno == "pay_B1")))
            .EstornadaEm.Should().NotBeNull();

        // Outra assinatura, só com a cobrança vencida.
        var pagoAte = DateTimeOffset.UtcNow.AddDays(-1);
        var (_, outroNegocio) = await NegocioContratadoAsync("sub_C", pagoAte);
        var vencimento = pagoAte.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd");
        _asaas.Responder("GET", "payments/pay_C1", 200,
            $$"""{"id":"pay_C1","subscription":"sub_C","value":49.9,"status":"OVERDUE","dueDate":"{{vencimento}}"}""");
        (await cliente.SendAsync(Evento("evt_c1", "PAYMENT_OVERDUE", pagamento: "pay_C1"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AssinaturaAsync(outroNegocio)).Estado.Should().Be(EstadoAssinatura.Atrasada);
    }

    [Fact]
    public async Task Cancelar_remove_no_asaas_e_usa_ate_o_fim_do_periodo_e_o_aviso_de_remocao_nao_muda_nada()
    {
        var (admin, negocioId) = await NegocioContratadoAsync("sub_D", pagoAte: DateTimeOffset.UtcNow.AddDays(20));
        _asaas.Responder("DELETE", "subscriptions/sub_D", 200, """{"deleted":true,"id":"sub_D"}""");

        var cancelar = await admin.PostAsync("/painel/assinatura/cancelar", null);

        cancelar.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancelar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imediato").GetBoolean().Should().BeFalse();
        _asaas.Recebidas.Should().Contain(r => r.Metodo == "DELETE" && r.Recurso == "subscriptions/sub_D");
        var assinatura = await AssinaturaAsync(negocioId);
        (assinatura.Estado, assinatura.CancelamentoAgendado).Should().Be((EstadoAssinatura.Ativa, true));
        (await admin.GetFromJsonAsync<DetalheAssinaturaNegocio>("/painel/assinatura", JsonApi))!.CancelamentoAte.Should().Be(assinatura.ProximoVencimento);

        // O Asaas avisa que a assinatura foi removida: já estava tratado.
        _asaas.Responder("GET", "subscriptions/sub_D", 200, """{"id":"sub_D","status":"ACTIVE","deleted":true}""");
        using var cliente = _fabrica.CreateClient();
        (await cliente.SendAsync(Evento("evt_d1", "SUBSCRIPTION_DELETED", assinatura: "sub_D"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _fabrica.NoBancoAsync(db => db.EventosWebhookPagamento.SingleAsync(e => e.IdEvento == "evt_d1"))).Resultado.Should().Be("JaCancelada");
        (await AssinaturaAsync(negocioId)).Estado.Should().Be(EstadoAssinatura.Ativa);
    }

    [Fact]
    public async Task Evento_desconhecido_ou_de_assinatura_que_nao_existe_responde_200_e_fica_registrado()
    {
        using var cliente = _fabrica.CreateClient();

        (await cliente.SendAsync(Evento("evt_x", "PAYMENT_CHECKOUT_VIEWED", pagamento: "pay_x"))).StatusCode.Should().Be(HttpStatusCode.OK);
        _asaas.Responder("GET", "payments/pay_y", 200, """{"id":"pay_y","subscription":"sub_nao_existe","value":10,"status":"RECEIVED","dueDate":"2026-10-01"}""");
        (await cliente.SendAsync(Evento("evt_y", "PAYMENT_RECEIVED", pagamento: "pay_y"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var resultados = await _fabrica.NoBancoAsync(db => db.EventosWebhookPagamento.ToDictionaryAsync(e => e.IdEvento, e => e.Resultado));
        resultados.Should().Contain("evt_x", "Ignorado").And.Contain("evt_y", "AssinaturaDesconhecida");
    }

    // ---------------------------------------------------------------- apoio

    private async Task<(HttpClient Admin, Guid NegocioId)> NegocioEmTesteAsync()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await _fabrica.NoBancoAsync(async db =>
        {
            var plano = await db.Planos.OrderBy(p => p.Ordem).FirstAsync();
            db.Assinaturas.Add(ServicoAssinatura.IniciarTeste(negocioId, plano, Periodicidade.Mensal, "teste", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });
        return (admin, negocioId);
    }

    /// <summary>Negócio já contratado no Asaas; com <paramref name="pagoAte"/>, ativo até essa data.</summary>
    private async Task<(HttpClient Admin, Guid NegocioId)> NegocioContratadoAsync(string idAssinatura, DateTimeOffset? pagoAte = null)
    {
        var (admin, negocioId) = await NegocioEmTesteAsync();
        await _fabrica.NoBancoAsync(async db =>
        {
            var assinatura = await db.Assinaturas.IgnoreQueryFilters().Include(a => a.Historico).SingleAsync(a => a.NegocioId == negocioId);
            ServicoAssinatura.RegistrarContratacao(assinatura, GatewayPagamentoAsaas.Nome, idAssinatura, "teste", DateTimeOffset.UtcNow);
            if (pagoAte is { } fim)
                db.CobrancasAssinatura.Add(ServicoAssinatura.RegistrarPagamento(
                    assinatura, 49.90m, FormaCobranca.Pix, fim.AddMonths(-1), fim.AddMonths(-1), fim, OrigemCobranca.Manual, null, "teste", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });
        return (admin, negocioId);
    }

    private Task<Assinatura> AssinaturaAsync(Guid negocioId) =>
        _fabrica.NoBancoAsync(db => db.Assinaturas.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.NegocioId == negocioId));

    private Task<Guid> PlanoAsync(string nome) => _fabrica.NoBancoAsync(db => db.Planos.Where(p => p.Nome == nome).Select(p => p.Id).SingleAsync());

    private static HttpRequestMessage Evento(string id, string nome, string? pagamento = null, string? assinatura = null, string token = Token)
    {
        var corpo = JsonSerializer.Serialize(new
        {
            id,
            @event = nome,
            dateCreated = "2026-10-28 10:00:00",
            payment = pagamento is null ? null : new { id = pagamento, value = 1.0 },
            subscription = assinatura is null ? null : new { id = assinatura },
        });
        var requisicao = new HttpRequestMessage(HttpMethod.Post, Webhook) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        requisicao.Headers.Add("asaas-access-token", token);
        return requisicao;
    }

    /// <summary>Asaas de mentira: respostas por "MÉTODO recurso" (a última configurada vale) e o registro do que chegou.</summary>
    private sealed class AsaasSimulado : HttpMessageHandler
    {
        private readonly Dictionary<string, (int Status, string Corpo)> _respostas = [];

        public List<(string Metodo, string Recurso, string? Corpo)> Recebidas { get; } = [];

        public void Responder(string metodo, string recurso, int status, string corpo)
        {
            lock (_respostas)
                _respostas[$"{metodo} {recurso}"] = (status, corpo);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recurso = request.RequestUri!.AbsolutePath.Replace("/v3/", string.Empty);
            var corpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            (int Status, string Corpo) resposta;
            lock (_respostas)
            {
                Recebidas.Add((request.Method.Method, recurso, corpo));
                resposta = _respostas.GetValueOrDefault($"{request.Method.Method} {recurso}", (404, "{}"));
            }

            return new HttpResponseMessage((HttpStatusCode)resposta.Status) { Content = new StringContent(resposta.Corpo, Encoding.UTF8, "application/json") };
        }
    }
}
