using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Notificacoes;

namespace Plataforma.Api.Controllers.Webhooks;

/// <summary>
/// Status de mensagem da instância do Evolution API (seção 8.1/10). A instância manda o segredo
/// compartilhado no cabeçalho <c>X-Webhook-Token</c> ou em <c>?token=</c> na URL configurada nela.
/// 404 se o provedor atual não é o Evolution API; 401 sem o segredo certo; 200 para evento novo,
/// repetido, irrelevante ou grande demais (a instância não pode ficar reenviando).
/// </summary>
[ApiController]
[Route("webhooks/evolution")]
public sealed class WebhooksEvolutionController : ControllerBase
{
    public const string CabecalhoToken = "X-Webhook-Token";

    private const int TamanhoMaximoCorpo = 256 * 1024;

    private readonly IProcessadorWebhookWhatsApp _processador;
    private readonly ILogger<WebhooksEvolutionController> _logger;

    public WebhooksEvolutionController(IProcessadorWebhookWhatsApp processador, ILogger<WebhooksEvolutionController> logger)
    {
        _processador = processador;
        _logger = logger;
    }

    /// <remarks>
    /// Ordem: 1) autentica (segredo em tempo constante) sem tocar no corpo; 2) lê o corpo com teto.
    /// Corpo acima do teto (ex.: <c>contacts.update</c> com foto, vindo do webhook global da instância) responde
    /// 200 sem processar — não é evento de status e a instância não pode ficar reenviando (um 4xx/5xx a faria
    /// repetir); registra só um Warning curto, sem pilha e sem conteúdo.
    /// </remarks>
    [HttpPost("status")]
    [RequestSizeLimit(TamanhoMaximoCorpo)]
    public async Task<IActionResult> Status([FromQuery] string? token, CancellationToken cancellationToken)
    {
        var tokenRecebido = Request.Headers.TryGetValue(CabecalhoToken, out var cabecalho) && !string.IsNullOrEmpty(cabecalho)
            ? cabecalho.ToString()
            : token;

        var acesso = _processador.Autenticar(tokenRecebido);
        if (acesso == ResultadoWebhookWhatsApp.NaoConfigurado) return NotFound();
        if (acesso == ResultadoWebhookWhatsApp.TokenInvalido) return Unauthorized();

        var corpo = await LerCorpoComTetoAsync(cancellationToken);
        if (corpo is null)
        {
            _logger.LogWarning("Webhook do Evolution ignorado: corpo acima de {Limite} bytes.", TamanhoMaximoCorpo);
            return Ok();
        }

        var resultado = await _processador.ProcessarAsync(tokenRecebido, corpo, cancellationToken);

        return resultado switch
        {
            ResultadoWebhookWhatsApp.NaoConfigurado => NotFound(),
            ResultadoWebhookWhatsApp.TokenInvalido => Unauthorized(),
            ResultadoWebhookWhatsApp.CorpoInvalido => BadRequest(),
            _ => Ok(),
        };
    }

    /// <summary>Lê o corpo até o teto; <c>null</c> se passar dele (tanto pelo Content-Length quanto pelo fluxo real).</summary>
    private async Task<string?> LerCorpoComTetoAsync(CancellationToken cancellationToken)
    {
        if (Request.ContentLength > TamanhoMaximoCorpo)
            return null;

        try
        {
            using var memoria = new MemoryStream();
            var bloco = new byte[8192];
            int lidos;
            while ((lidos = await Request.Body.ReadAsync(bloco, cancellationToken)) > 0)
            {
                if (memoria.Length + lidos > TamanhoMaximoCorpo)
                    return null;
                memoria.Write(bloco, 0, lidos);
            }

            return System.Text.Encoding.UTF8.GetString(memoria.GetBuffer(), 0, (int)memoria.Length);
        }
        catch (BadHttpRequestException excecao) when (excecao.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }
    }
}
