using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Assinaturas;

namespace Plataforma.Api.Controllers.Webhooks;

/// <summary>
/// Webhook do gateway de cobrança (seção 7). 404 enquanto nenhum provedor com webhook estiver
/// configurado; 401 com assinatura inválida; 200 para evento novo ou repetido (repetido não é
/// reprocessado, mas o provedor não pode ficar reenviando).
/// </summary>
[ApiController]
[Route("webhooks/pagamentos")]
public sealed class WebhooksPagamentoController : ControllerBase
{
    private const int TamanhoMaximoCorpo = 256 * 1024;

    private readonly IProcessadorWebhookPagamento _processador;

    public WebhooksPagamentoController(IProcessadorWebhookPagamento processador) => _processador = processador;

    [HttpPost("{provedor}")]
    [RequestSizeLimit(TamanhoMaximoCorpo)]
    public async Task<IActionResult> Receber(string provedor, CancellationToken cancellationToken)
    {
        using var leitor = new StreamReader(Request.Body);
        var corpo = await leitor.ReadToEndAsync(cancellationToken);
        var cabecalhos = Request.Headers.ToDictionary(c => c.Key.ToLowerInvariant(), c => c.Value.ToString());

        var resultado = await _processador.ProcessarAsync(provedor, new RequisicaoWebhook(cabecalhos, corpo), cancellationToken);

        return resultado switch
        {
            ResultadoWebhook.ProvedorDesconhecido => NotFound(),
            ResultadoWebhook.AssinaturaInvalida => Unauthorized(),
            _ => Ok(),
        };
    }
}
