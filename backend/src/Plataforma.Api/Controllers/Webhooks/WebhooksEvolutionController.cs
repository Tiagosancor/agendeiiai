using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Notificacoes;

namespace Plataforma.Api.Controllers.Webhooks;

/// <summary>
/// Status de mensagem da instância do Evolution API (seção 8.1/10). A instância manda o segredo
/// compartilhado no cabeçalho <c>X-Webhook-Token</c> ou em <c>?token=</c> na URL configurada nela.
/// 404 se o provedor atual não é o Evolution API; 401 sem o segredo certo; 200 para evento novo,
/// repetido ou irrelevante (a instância não pode ficar reenviando).
/// </summary>
[ApiController]
[Route("webhooks/evolution")]
public sealed class WebhooksEvolutionController : ControllerBase
{
    public const string CabecalhoToken = "X-Webhook-Token";

    private const int TamanhoMaximoCorpo = 256 * 1024;

    private readonly IProcessadorWebhookWhatsApp _processador;

    public WebhooksEvolutionController(IProcessadorWebhookWhatsApp processador) => _processador = processador;

    [HttpPost("status")]
    [RequestSizeLimit(TamanhoMaximoCorpo)]
    public async Task<IActionResult> Status([FromQuery] string? token, CancellationToken cancellationToken)
    {
        using var leitor = new StreamReader(Request.Body);
        var corpo = await leitor.ReadToEndAsync(cancellationToken);

        var tokenRecebido = Request.Headers.TryGetValue(CabecalhoToken, out var cabecalho) && !string.IsNullOrEmpty(cabecalho)
            ? cabecalho.ToString()
            : token;

        var resultado = await _processador.ProcessarAsync(tokenRecebido, corpo, cancellationToken);

        return resultado switch
        {
            ResultadoWebhookWhatsApp.NaoConfigurado => NotFound(),
            ResultadoWebhookWhatsApp.TokenInvalido => Unauthorized(),
            ResultadoWebhookWhatsApp.CorpoInvalido => BadRequest(),
            _ => Ok(),
        };
    }
}
