using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Evolution API v2 para o agente. Cabeçalho <c>apikey</c> (chave global). Payloads conforme a documentação da
/// v2 (confirmada só em espelhos da doc — a oficial estava fora do ar; conferir contra a instância real):
/// <c>POST /instance/create</c>, <c>GET /instance/connect/{i}</c>, <c>GET /instance/connectionState/{i}</c>,
/// <c>POST /message/sendText/{i}</c>. Nunca loga o texto das mensagens nem o telefone.
/// </summary>
public sealed class ClienteEvolution : IClienteEvolution
{
    /// <summary>Eventos do webhook por instância. MESSAGES_UPDATE (status de entrega) fica de fora: é do webhook antigo.</summary>
    public static readonly string[] EventosDoWebhook = ["MESSAGES_UPSERT", "QRCODE_UPDATED", "CONNECTION_UPDATE"];

    private readonly HttpClient _httpClient;
    private readonly OpcoesEvolution _opcoes;
    private readonly ILogger<ClienteEvolution> _logger;

    public ClienteEvolution(HttpClient httpClient, IOptions<OpcoesEvolution> opcoes, ILogger<ClienteEvolution> logger)
    {
        _httpClient = httpClient;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public async Task<string?> CriarInstanciaAsync(string nomeInstancia, string? urlWebhook, CancellationToken cancellationToken = default)
    {
        object corpo = urlWebhook is null
            ? new { instanceName = nomeInstancia, qrcode = true, integration = "WHATSAPP-BAILEYS" }
            : new
            {
                instanceName = nomeInstancia,
                qrcode = true,
                integration = "WHATSAPP-BAILEYS",
                // TODO: confirmar na instância real que o create aceita o objeto "webhook" aninhado (doc v2) — se não aceitar,
                // chamar POST /webhook/set/{instância} logo depois.
                webhook = new { enabled = true, url = urlWebhook, byEvents = false, base64 = false, events = EventosDoWebhook },
            };

        using var resposta = await EnviarAsync(HttpMethod.Post, "instance/create", corpo, cancellationToken);
        if (!resposta.IsSuccessStatusCode)
            throw new FalhaEvolutionException($"A Evolution recusou a criação da instância (HTTP {(int)resposta.StatusCode}).");

        return await LerQrCodeAsync(resposta, cancellationToken);
    }

    public async Task<string?> ObterQrCodeAsync(string nomeInstancia, CancellationToken cancellationToken = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"instance/connect/{Uri.EscapeDataString(nomeInstancia)}", null, cancellationToken);
        if (!resposta.IsSuccessStatusCode)
            throw new FalhaEvolutionException($"A Evolution recusou o pedido de conexão (HTTP {(int)resposta.StatusCode}).");

        return await LerQrCodeAsync(resposta, cancellationToken);
    }

    public async Task<EstadoInstanciaEvolution> ConsultarStatusAsync(string nomeInstancia, CancellationToken cancellationToken = default)
    {
        using var resposta = await EnviarAsync(HttpMethod.Get, $"instance/connectionState/{Uri.EscapeDataString(nomeInstancia)}", null, cancellationToken);

        if (resposta.StatusCode == HttpStatusCode.NotFound)
            return EstadoInstanciaEvolution.NaoExiste;

        if (!resposta.IsSuccessStatusCode)
            throw new FalhaEvolutionException($"A Evolution recusou a consulta de status (HTTP {(int)resposta.StatusCode}).");

        try
        {
            using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(cancellationToken));
            var raiz = documento.RootElement;
            var estado = raiz.TryGetProperty("instance", out var instancia) && instancia.TryGetProperty("state", out var valor)
                ? valor.GetString()
                : raiz.TryGetProperty("state", out var direto) ? direto.GetString() : null;

            return estado?.ToLowerInvariant() switch
            {
                "open" => EstadoInstanciaEvolution.Aberta,
                "connecting" => EstadoInstanciaEvolution.Conectando,
                _ => EstadoInstanciaEvolution.Fechada,
            };
        }
        catch (JsonException excecao)
        {
            throw new FalhaEvolutionException("Resposta de status da Evolution ilegível.", excecao);
        }
    }

    public async Task<string?> EnviarTextoAsync(string nomeInstancia, string telefoneE164, string texto, CancellationToken cancellationToken = default)
    {
        var corpo = new { number = telefoneE164.TrimStart('+'), text = texto };

        using var resposta = await EnviarAsync(HttpMethod.Post, $"message/sendText/{Uri.EscapeDataString(nomeInstancia)}", corpo, cancellationToken);
        if (!resposta.IsSuccessStatusCode)
            throw new FalhaEvolutionException($"A Evolution recusou o envio (HTTP {(int)resposta.StatusCode}).");

        try
        {
            using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(cancellationToken));
            return documento.RootElement.TryGetProperty("key", out var chave) && chave.TryGetProperty("id", out var id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Falha de rede, timeout e 5xx viram <see cref="FalhaEvolutionException"/>; 4xx volta para quem chamou decidir.</summary>
    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string caminho, object? corpo, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_opcoes.BaseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(_opcoes.ApiKey))
            throw new FalhaEvolutionException("Evolution__BaseUrl e Evolution__ApiKey não estão configurados.");

        // O caminho leva o nome da instância; no log vai só a operação (primeiro segmento + segundo).
        var operacao = string.Join('/', caminho.Split('/').Take(2).Select(p => p.StartsWith("neg-", StringComparison.Ordinal) ? "{i}" : p));

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(TimeSpan.FromSeconds(_opcoes.TimeoutSegundos));

        using var requisicao = new HttpRequestMessage(metodo, new Uri($"{baseUri.ToString().TrimEnd('/')}/{caminho}"));
        requisicao.Headers.Add("apikey", _opcoes.ApiKey);
        if (corpo is not null)
            requisicao.Content = JsonContent.Create(corpo);

        try
        {
            var resposta = await _httpClient.SendAsync(requisicao, limite.Token);

            if ((int)resposta.StatusCode >= 500)
            {
                var status = (int)resposta.StatusCode;
                resposta.Dispose();
                _logger.LogWarning("Evolution API respondeu HTTP {Status} em {Operacao}.", status, operacao);
                throw new FalhaEvolutionException($"A Evolution respondeu HTTP {status}.");
            }

            return resposta;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Evolution API não respondeu em {Segundos} s ({Operacao}).", _opcoes.TimeoutSegundos, operacao);
            throw new FalhaEvolutionException("A Evolution não respondeu a tempo.");
        }
        catch (HttpRequestException excecao)
        {
            _logger.LogWarning("Evolution API fora do ar ({Operacao}): {Erro}.", operacao, excecao.Message);
            throw new FalhaEvolutionException("A Evolution está fora do ar.", excecao);
        }
    }

    /// <summary>QR code em <c>qrcode.base64</c> (create/connect, doc) ou <c>base64</c> (algumas versões do connect) — aceita os dois.</summary>
    private static async Task<string?> LerQrCodeAsync(HttpResponseMessage resposta, CancellationToken cancellationToken)
    {
        try
        {
            using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(cancellationToken));
            var raiz = documento.RootElement;

            if (raiz.TryGetProperty("qrcode", out var qr) && qr.ValueKind == JsonValueKind.Object
                && qr.TryGetProperty("base64", out var base64Aninhado) && base64Aninhado.ValueKind == JsonValueKind.String)
                return base64Aninhado.GetString();

            return raiz.TryGetProperty("base64", out var base64) && base64.ValueKind == JsonValueKind.String ? base64.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
