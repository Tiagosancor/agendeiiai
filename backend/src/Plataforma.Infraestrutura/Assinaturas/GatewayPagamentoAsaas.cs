using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Assinaturas;

/// <summary>
/// Asaas (API v3) como gateway da assinatura. Único lugar do sistema que conhece o Asaas:
/// <list type="bullet">
/// <item>Pagador: <c>POST customers</c> (nome, CPF/CNPJ, e-mail) — o documento vai só para lá.</item>
/// <item>Assinatura: <c>POST subscriptions</c> com <c>billingType: UNDEFINED</c> — o pagador escolhe Pix, boleto ou cartão na
/// fatura hospedada do Asaas (<c>invoiceUrl</c>); nenhum dado de cartão passa por aqui. Troca de plano:
/// <c>PUT subscriptions/{id}</c> sem <c>updatePendingPayments</c> (vale a partir da próxima cobrança). Cancelar:
/// <c>DELETE subscriptions/{id}</c> (o Asaas apaga as cobranças pendentes).</item>
/// <item>Webhook: token no cabeçalho <c>asaas-access-token</c>, comparado em tempo constante. O corpo só diz QUAL cobrança
/// ou assinatura mudou: o estado vem de uma consulta à própria API (nunca confia só no corpo — seção 8.6.6).</item>
/// </list>
/// Nova tentativa (até 3, com espera crescente) só em leitura e em operações idempotentes (GET, PUT, DELETE) com falha de
/// rede, timeout ou 5xx; criar (POST) nunca repete, para não duplicar cliente ou assinatura. Log só com método, recurso e
/// status — nunca corpo, documento ou chave.
/// </summary>
public sealed class GatewayPagamentoAsaas : IGatewayPagamento
{
    public const string Nome = "asaas";
    public const string CabecalhoTokenWebhook = "asaas-access-token";

    private const int MaximoTentativas = 3;
    private const string MensagemIndisponivel =
        "Não foi possível falar com o serviço de pagamento agora. Tente de novo em alguns minutos.";

    /// <summary>Datas do Asaas ("yyyy-MM-dd") são dias no horário de Brasília.</summary>
    private static readonly TimeSpan FusoAsaas = TimeSpan.FromHours(-3);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] StatusPagos = ["CONFIRMED", "RECEIVED", "RECEIVED_IN_CASH"];
    private static readonly string[] StatusEstornados = ["REFUNDED", "CHARGEBACK_REQUESTED", "CHARGEBACK_DISPUTE", "AWAITING_CHARGEBACK_REVERSAL"];
    private static readonly string[] StatusEmAberto = ["PENDING", "OVERDUE"];

    private readonly HttpClient _http;
    private readonly OpcoesAsaas _opcoes;
    private readonly string _userAgent;
    private readonly ILogger<GatewayPagamentoAsaas> _logger;

    public GatewayPagamentoAsaas(
        HttpClient http, IOptions<OpcoesAsaas> opcoes, IOptions<OpcoesMarca> marca, ILogger<GatewayPagamentoAsaas> logger)
    {
        _http = http;
        _opcoes = opcoes.Value;
        _userAgent = string.IsNullOrWhiteSpace(_opcoes.NomeAplicacao) ? marca.Value.NomeProduto : _opcoes.NomeAplicacao;
        _logger = logger;
    }

    public string Provedor => Nome;

    /// <summary>Sem chave e token configurados, o webhook do Asaas responde 404 (como um provedor inexistente).</summary>
    public bool AceitaWebhook => _opcoes.Configurado;

    public async Task<string?> CriarClienteAsync(DadosClienteGateway dados, CancellationToken cancellationToken = default)
    {
        var cliente = await EnviarAsync<RecursoAsaas>(HttpMethod.Post, "customers", new
        {
            name = dados.NomeNegocio,
            cpfCnpj = dados.CpfCnpj,
            email = dados.Email,
            externalReference = dados.NegocioId.ToString(),
        }, cancellationToken);
        return cliente?.Id;
    }

    public async Task<string?> CriarOuAlterarAssinaturaAsync(DadosAssinaturaGateway dados, CancellationToken cancellationToken = default)
    {
        var descricao = $"Plano {dados.NomePlano} ({dados.Periodicidade.ToLowerInvariant()})";

        if (dados.IdExternoAssinatura is { } id)
        {
            await EnviarAsync<RecursoAsaas>(HttpMethod.Put, $"subscriptions/{Uri.EscapeDataString(id)}", new
            {
                value = dados.ValorDoPeriodo,
                cycle = Ciclo(dados.Periodicidade),
                description = descricao,
                updatePendingPayments = false,
            }, cancellationToken);
            return id;
        }

        var vencimento = dados.PrimeiroVencimento ?? DateTimeOffset.UtcNow;
        var criada = await EnviarAsync<RecursoAsaas>(HttpMethod.Post, "subscriptions", new
        {
            customer = dados.IdExternoCliente,
            billingType = "UNDEFINED",
            value = dados.ValorDoPeriodo,
            nextDueDate = vencimento.ToOffset(FusoAsaas).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            cycle = Ciclo(dados.Periodicidade),
            description = descricao,
            externalReference = (dados.AssinaturaId ?? dados.NegocioId).ToString(),
        }, cancellationToken);
        return criada?.Id;
    }

    public async Task CancelarAssinaturaAsync(string? idExternoAssinatura, CancellationToken cancellationToken = default)
    {
        if (idExternoAssinatura is null)
            return;

        // Já removida lá (404) é o mesmo resultado.
        await EnviarAsync<RecursoAsaas>(HttpMethod.Delete, $"subscriptions/{Uri.EscapeDataString(idExternoAssinatura)}", null, cancellationToken);
    }

    public async Task<InstrucoesPagamento> GerarLinkPagamentoAsync(DadosCobrancaGateway dados, CancellationToken cancellationToken = default)
    {
        if (dados.IdExternoAssinatura is not { } id)
            return new InstrucoesPagamento(null, null, null, "Contrate o plano para gerar a cobrança.");

        var pagina = await EnviarAsync<ListaAsaas<PagamentoAsaas>>(
            HttpMethod.Get, $"subscriptions/{Uri.EscapeDataString(id)}/payments", null, cancellationToken);
        var emAberto = (pagina?.Data ?? [])
            .Where(p => StatusEmAberto.Contains(p.Status) && p.InvoiceUrl is not null)
            .OrderBy(p => p.DueDate)
            .FirstOrDefault();

        if (emAberto is null)
            return new InstrucoesPagamento(null, null, null,
                "Nenhuma cobrança em aberto agora. A próxima é gerada alguns dias antes do vencimento.");

        var vencimento = emAberto.DueDate is { } dia ? DateTime.Parse(dia, CultureInfo.InvariantCulture).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : null;
        var texto = $"Cobrança de {FormatacaoBrasil.Reais(emAberto.Value)}"
            + (vencimento is null ? "" : $" com vencimento em {vencimento}")
            + ". Pague por Pix, boleto ou cartão na página segura do Asaas.";
        return new InstrucoesPagamento(emAberto.InvoiceUrl, null, null, texto);
    }

    public async Task<EventoPagamentoGateway?> InterpretarWebhookAsync(RequisicaoWebhook requisicao, CancellationToken cancellationToken = default)
    {
        if (!TokenValido(requisicao.Cabecalhos.GetValueOrDefault(CabecalhoTokenWebhook)))
            return null;

        EventoWebhookAsaas? evento;
        try
        {
            evento = JsonSerializer.Deserialize<EventoWebhookAsaas>(requisicao.Corpo, Json);
        }
        catch (JsonException)
        {
            evento = null;
        }

        // Sem id não dá para deduplicar pelo evento: usa o hash do corpo (o mesmo corpo reenviado cai no mesmo registro).
        var idEvento = evento?.Id ?? "sem-id:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requisicao.Corpo)));
        var nome = evento?.Event;
        var ignorado = new EventoPagamentoGateway(idEvento, TipoEventoPagamento.Ignorado, null, null, null, null, null, null, Nome: nome);

        switch (nome)
        {
            case "PAYMENT_CONFIRMED" or "PAYMENT_RECEIVED" when evento?.Payment?.Id is { } pagamentoId:
            {
                var pagamento = await ConsultarPagamentoAsync(pagamentoId, cancellationToken);
                if (pagamento is null || !StatusPagos.Contains(pagamento.Status))
                    return ignorado;
                var pagoEm = Dia(pagamento.PaymentDate ?? pagamento.ConfirmedDate ?? pagamento.ClientPaymentDate) ?? DateTimeOffset.UtcNow;
                return new EventoPagamentoGateway(
                    idEvento, TipoEventoPagamento.PagamentoConfirmado, pagamento.Subscription, pagamento.Id, pagamento.Value, pagoEm,
                    null, null, Dia(pagamento.DueDate), Forma(pagamento.BillingType), nome);
            }

            case "PAYMENT_OVERDUE" when evento?.Payment?.Id is { } pagamentoId:
            {
                var pagamento = await ConsultarPagamentoAsync(pagamentoId, cancellationToken);
                if (pagamento is null || pagamento.Status != "OVERDUE")
                    return ignorado;
                return new EventoPagamentoGateway(
                    idEvento, TipoEventoPagamento.CobrancaVencida, pagamento.Subscription, pagamento.Id, pagamento.Value, null,
                    null, null, Dia(pagamento.DueDate), null, nome);
            }

            case "PAYMENT_REFUNDED" or "PAYMENT_CHARGEBACK_REQUESTED" when evento?.Payment?.Id is { } pagamentoId:
            {
                var pagamento = await ConsultarPagamentoAsync(pagamentoId, cancellationToken);
                if (pagamento is null || !StatusEstornados.Contains(pagamento.Status))
                    return ignorado;
                return new EventoPagamentoGateway(
                    idEvento, TipoEventoPagamento.PagamentoEstornado, pagamento.Subscription, pagamento.Id, pagamento.Value, null,
                    null, null, null, null, nome);
            }

            case "SUBSCRIPTION_DELETED" or "SUBSCRIPTION_INACTIVATED" when evento?.Subscription?.Id is { } assinaturaId:
            {
                var assinatura = await EnviarAsync<AssinaturaAsaas>(
                    HttpMethod.Get, $"subscriptions/{Uri.EscapeDataString(assinaturaId)}", null, cancellationToken);
                var encerrada = assinatura is null || assinatura.Deleted || assinatura.Status is "INACTIVE" or "EXPIRED";
                return encerrada
                    ? new EventoPagamentoGateway(idEvento, TipoEventoPagamento.AssinaturaCancelada, assinaturaId, null, null, null, null, null, Nome: nome)
                    : ignorado;
            }

            default:
                return ignorado;
        }
    }

    // ---------------------------------------------------------------- apoio

    private bool TokenValido(string? recebido)
    {
        if (string.IsNullOrEmpty(recebido) || string.IsNullOrEmpty(_opcoes.TokenWebhook))
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebido), Encoding.UTF8.GetBytes(_opcoes.TokenWebhook));
    }

    private Task<PagamentoAsaas?> ConsultarPagamentoAsync(string id, CancellationToken cancellationToken) =>
        EnviarAsync<PagamentoAsaas>(HttpMethod.Get, $"payments/{Uri.EscapeDataString(id)}", null, cancellationToken);

    /// <summary>
    /// Uma chamada à API. 404 → nulo. 4xx → <see cref="FalhaGatewayPagamentoException"/> recusada, com a descrição do Asaas.
    /// Rede/timeout/5xx → nova tentativa se idempotente; senão (ou esgotadas) falha de indisponibilidade.
    /// </summary>
    private async Task<T?> EnviarAsync<T>(HttpMethod metodo, string recurso, object? corpo, CancellationToken cancellationToken)
        where T : class
    {
        var idempotente = metodo != HttpMethod.Post;
        var tentativas = idempotente ? MaximoTentativas : 1;
        var recursoLog = RecursoParaLog(recurso);

        for (var tentativa = 1; ; tentativa++)
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limite.CancelAfter(TimeSpan.FromSeconds(_opcoes.TimeoutSegundos));
            try
            {
                using var requisicao = new HttpRequestMessage(metodo, recurso);
                requisicao.Headers.TryAddWithoutValidation("access_token", _opcoes.ChaveApi);
                requisicao.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
                if (corpo is not null)
                    requisicao.Content = JsonContent.Create(corpo, options: Json);

                using var resposta = await _http.SendAsync(requisicao, limite.Token);
                _logger.LogInformation("Asaas {Metodo} {Recurso} → HTTP {Status}.", metodo.Method, recursoLog, (int)resposta.StatusCode);

                if (resposta.IsSuccessStatusCode)
                    return await resposta.Content.ReadFromJsonAsync<T>(Json, limite.Token);

                if (resposta.StatusCode == HttpStatusCode.NotFound)
                    return null;

                if ((int)resposta.StatusCode < 500)
                    throw new FalhaGatewayPagamentoException(await DescricaoDoErroAsync(resposta, limite.Token), recusado: true);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Asaas {Metodo} {Recurso} não respondeu em {Segundos} s (tentativa {Tentativa}).",
                    metodo.Method, recursoLog, _opcoes.TimeoutSegundos, tentativa);
            }
            catch (HttpRequestException)
            {
                _logger.LogWarning("Asaas {Metodo} {Recurso}: falha de conexão (tentativa {Tentativa}).", metodo.Method, recursoLog, tentativa);
            }

            if (tentativa >= tentativas)
                throw new FalhaGatewayPagamentoException(MensagemIndisponivel);

            await Task.Delay(TimeSpan.FromMilliseconds(500 * tentativa * tentativa), cancellationToken);
        }
    }

    /// <summary>A primeira descrição de <c>errors[]</c> (texto do Asaas, em português); nunca o corpo inteiro.</summary>
    private static async Task<string> DescricaoDoErroAsync(HttpResponseMessage resposta, CancellationToken cancellationToken)
    {
        try
        {
            var erro = await resposta.Content.ReadFromJsonAsync<ErroAsaas>(Json, cancellationToken);
            if (erro?.Errors?.FirstOrDefault()?.Description is { Length: > 0 } descricao)
                return descricao;
        }
        catch (JsonException)
        {
        }

        return "O serviço de pagamento recusou o pedido. Confira os dados e tente de novo.";
    }

    /// <summary>"subscriptions/sub_123/payments" → "subscriptions/{id}/payments": o log não precisa do ID.</summary>
    private static string RecursoParaLog(string recurso) =>
        string.Join('/', recurso.Split('/').Select((parte, i) => i % 2 == 1 ? "{id}" : parte));

    /// <summary>Anual: uma cobrança por ano (preço por mês × 12, calculado em <c>Assinatura.ValorDoPeriodo</c>).</summary>
    private static string Ciclo(string periodicidade) =>
        string.Equals(periodicidade, "Anual", StringComparison.OrdinalIgnoreCase) ? "YEARLY" : "MONTHLY";

    private static DateTimeOffset? Dia(string? data) =>
        DateOnly.TryParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), FusoAsaas).ToUniversalTime()
            : null;

    private static string? Forma(string? billingType) => billingType switch
    {
        "PIX" => "Pix",
        "BOLETO" => "Boleto",
        "CREDIT_CARD" or "DEBIT_CARD" => "Cartao",
        "TRANSFER" or "DEPOSIT" => "Transferencia",
        _ => null,
    };

    // ---------------------------------------------------------------- formatos da API (só o que é lido)

    private sealed record RecursoAsaas(string? Id);

    private sealed record ListaAsaas<T>(List<T>? Data);

    private sealed record PagamentoAsaas(
        string Id, string? Subscription, decimal Value, string Status, string? DueDate, string? PaymentDate, string? ConfirmedDate,
        string? ClientPaymentDate, string? InvoiceUrl, string? BillingType);

    private sealed record AssinaturaAsaas(string Id, string? Status, bool Deleted);

    private sealed record ItemErroAsaas(string? Code, string? Description);

    private sealed record ErroAsaas(List<ItemErroAsaas>? Errors);

    private sealed record ReferenciaAsaas(string? Id);

    private sealed record EventoWebhookAsaas(string? Id, string? Event, ReferenciaAsaas? Payment, ReferenciaAsaas? Subscription);
}
