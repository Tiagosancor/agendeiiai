using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Notificacoes;

/// <summary>
/// Evolution API (seção 4) — provedor NÃO oficial: viola os termos do WhatsApp e pode levar ao
/// banimento do número, por isso só sobe com <c>WhatsApp:PermitirNaoOficial</c> e nunca é o único
/// canal do código (o e-mail sai em paralelo — seção 8.1).
/// <para>
/// Cada tentativa tem timeout curto (<see cref="OpcoesEvolutionApi.TimeoutSegundos"/>) e há no
/// máximo UMA nova tentativa, só para falha de rede (conexão recusada/caída) ou erro 5xx da
/// instância. Timeout NÃO repete: com a sessão do WhatsApp caída, a instância segura o
/// <c>sendText</c> sem responder (medido: 20 s+), e repetir só dobraria a espera do cliente no
/// "enviar código". Recusa (4xx, ex.: número sem WhatsApp) também não adianta repetir. Depois
/// disso, o canal é dado como indisponível; nunca lança (um canal não pode derrubar o outro).
/// </para>
/// </summary>
public sealed class MensageriaWhatsAppEvolution : IMensageriaWhatsApp
{
    private const int MaximoTentativas = 2;

    private readonly HttpClient _httpClient;
    private readonly OpcoesEvolutionApi _opcoes;
    private readonly ILogger<MensageriaWhatsAppEvolution> _logger;

    public MensageriaWhatsAppEvolution(HttpClient httpClient, IOptions<OpcoesWhatsApp> opcoes, ILogger<MensageriaWhatsAppEvolution> logger)
    {
        _httpClient = httpClient;
        _opcoes = opcoes.Value.Evolution;
        _logger = logger;
    }

    public async Task<ResultadoEnvioWhatsApp> EnviarAsync(TelefoneE164 telefone, string mensagem, CancellationToken cancellationToken = default)
    {
        var url = $"{_opcoes.UrlBase!.TrimEnd('/')}/message/sendText/{Uri.EscapeDataString(_opcoes.NomeInstancia!)}";
        var corpo = new { number = telefone.Valor.TrimStart('+'), text = mensagem };

        for (var tentativa = 1; tentativa <= MaximoTentativas; tentativa++)
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limite.CancelAfter(TimeSpan.FromSeconds(_opcoes.TimeoutSegundos));

            try
            {
                using var requisicao = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
                requisicao.Headers.Add("apikey", _opcoes.ChaveApi);

                using var resposta = await _httpClient.SendAsync(requisicao, limite.Token);

                if (resposta.IsSuccessStatusCode)
                {
                    var idMensagem = await LerIdMensagemAsync(resposta, limite.Token);
                    return idMensagem is null ? ResultadoEnvioWhatsApp.Enviado() : ResultadoEnvioWhatsApp.AguardandoConfirmacao(idMensagem);
                }

                if ((int)resposta.StatusCode < 500)
                {
                    // Nunca o telefone em texto puro no log (seção 8.1.6).
                    _logger.LogWarning("Evolution API recusou a mensagem para {Telefone}: HTTP {Status}.", telefone.Mascarado(), (int)resposta.StatusCode);
                    return ResultadoEnvioWhatsApp.Indisponivel();
                }

                _logger.LogWarning("Evolution API respondeu HTTP {Status} (tentativa {Tentativa}/{Maximo}).", (int)resposta.StatusCode, tentativa, MaximoTentativas);
            }
            catch (OperationCanceledException)
            {
                if (!cancellationToken.IsCancellationRequested)
                    _logger.LogWarning("Evolution API não respondeu em {Segundos} s — sessão do WhatsApp caída? Sem nova tentativa.", _opcoes.TimeoutSegundos);

                return ResultadoEnvioWhatsApp.Indisponivel();
            }
            catch (HttpRequestException excecao)
            {
                _logger.LogWarning("Evolution API fora do ar (tentativa {Tentativa}/{Maximo}): {Erro}.",
                    tentativa, MaximoTentativas, excecao.Message);
            }
            catch (Exception excecao)
            {
                _logger.LogError(excecao, "Erro inesperado ao enviar WhatsApp pela Evolution API.");
                return ResultadoEnvioWhatsApp.Indisponivel();
            }
        }

        return ResultadoEnvioWhatsApp.Indisponivel();
    }

    /// <summary>
    /// <c>GET /instance/connectionState/{instância}</c>: <c>open</c> = conectada, <c>connecting</c> =
    /// esperando a leitura do QR code, <c>close</c> = desconectada.
    /// </summary>
    public async Task<(EstadoConexaoWhatsApp Estado, string? Detalhe)> ConsultarConexaoAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{_opcoes.UrlBase!.TrimEnd('/')}/instance/connectionState/{Uri.EscapeDataString(_opcoes.NomeInstancia!)}";

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(TimeSpan.FromSeconds(_opcoes.TimeoutSegundos));

        try
        {
            using var requisicao = new HttpRequestMessage(HttpMethod.Get, url);
            requisicao.Headers.Add("apikey", _opcoes.ChaveApi);
            using var resposta = await _httpClient.SendAsync(requisicao, limite.Token);

            if (resposta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (EstadoConexaoWhatsApp.Indisponivel, "A instância recusou a chave de API.");

            if (resposta.StatusCode == HttpStatusCode.NotFound)
                return (EstadoConexaoWhatsApp.Indisponivel, $"Instância \"{_opcoes.NomeInstancia}\" não encontrada.");

            if (!resposta.IsSuccessStatusCode)
                return (EstadoConexaoWhatsApp.Indisponivel, $"A instância respondeu HTTP {(int)resposta.StatusCode}.");

            using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(limite.Token));
            var estado = documento.RootElement.TryGetProperty("instance", out var instancia) && instancia.TryGetProperty("state", out var valor)
                ? valor.GetString()
                : documento.RootElement.TryGetProperty("state", out var raiz) ? raiz.GetString() : null;

            return estado?.ToLowerInvariant() switch
            {
                "open" => (EstadoConexaoWhatsApp.Conectada, null),
                "connecting" => (EstadoConexaoWhatsApp.AguardandoQrCode, "Leia o QR code novo no gerenciador da instância."),
                "close" => (EstadoConexaoWhatsApp.Desconectada, "A sessão do WhatsApp caiu. Reconecte a instância."),
                _ => (EstadoConexaoWhatsApp.Indisponivel, $"Estado desconhecido: {estado ?? "vazio"}."),
            };
        }
        catch (Exception excecao) when (excecao is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            return (EstadoConexaoWhatsApp.Indisponivel, "A instância não respondeu.");
        }
    }

    /// <summary>Resposta do <c>sendText</c>: <c>{ "key": { "id": "..." } }</c>. Corpo fora desse formato não é erro — só não há ID para o webhook.</summary>
    private static async Task<string?> LerIdMensagemAsync(HttpResponseMessage resposta, CancellationToken cancellationToken)
    {
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
}
