using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Atendimento humano das conversas do WhatsApp (agente, Fase 4). Sempre no negócio do token — nenhuma rota recebe negócio, e conversa
/// de outro negócio responde 404 (igual a uma que não existe). Ficam fora desta fatia: lista, detalhe e apagar.
/// </summary>
[ApiController]
[Route("painel/whatsapp/conversas")]
[Authorize(Policy = nameof(Permissao.AtenderConversasWhatsApp))]
public sealed class ConversasWhatsAppController : ControllerBase
{
    private readonly IGerenciadorConversasWhatsApp _conversas;

    public ConversasWhatsAppController(IGerenciadorConversasWhatsApp conversas) => _conversas = conversas;

    /// <summary>O agente fica em silêncio nesta conversa. Idempotente (204 também se já estava assumida).</summary>
    [HttpPost("{id:guid}/assumir")]
    public async Task<IActionResult> Assumir(Guid id, CancellationToken cancellationToken) =>
        await _conversas.AssumirAsync(id, cancellationToken) == ResultadoConversaWhatsApp.Ok ? NoContent() : NotFound();

    /// <summary>Devolve ao agente sem responder o que se acumulou. Idempotente (204 também se já estava com o agente).</summary>
    [HttpPost("{id:guid}/devolver")]
    public async Task<IActionResult> Devolver(Guid id, CancellationToken cancellationToken) =>
        await _conversas.DevolverAsync(id, cancellationToken) == ResultadoConversaWhatsApp.Ok ? NoContent() : NotFound();
}
