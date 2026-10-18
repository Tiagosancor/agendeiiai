using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.WhatsAppAgente;

namespace Plataforma.Api.Controllers.Webhooks;

/// <summary>
/// Webhook por instância do agente de WhatsApp (Evolution). O segredo vai no caminho; 401 se não bate (comparação em
/// tempo constante), 404 se o segredo não está configurado, 200 para evento novo, repetido ou irrelevante (a Evolution
/// não pode ficar reenviando). Outra rota que o webhook antigo de status (<c>/webhooks/evolution/status</c>).
/// </summary>
[ApiController]
[Route("webhooks/whatsapp")]
public sealed class WebhooksWhatsAppAgenteController : ControllerBase
{
    private const int TamanhoMaximoCorpo = 512 * 1024;

    private readonly IProcessadorWebhookAgente _processador;

    public WebhooksWhatsAppAgenteController(IProcessadorWebhookAgente processador) => _processador = processador;

    [HttpPost("{segredo}")]
    [RequestSizeLimit(TamanhoMaximoCorpo)]
    public async Task<IActionResult> Receber(string segredo, CancellationToken cancellationToken)
    {
        using var leitor = new StreamReader(Request.Body);
        var corpo = await leitor.ReadToEndAsync(cancellationToken);

        return await _processador.ProcessarAsync(segredo, corpo, cancellationToken) switch
        {
            ResultadoWebhookAgente.NaoConfigurado => NotFound(),
            ResultadoWebhookAgente.SegredoInvalido => Unauthorized(),
            ResultadoWebhookAgente.CorpoInvalido => BadRequest(),
            _ => Ok(),
        };
    }
}
