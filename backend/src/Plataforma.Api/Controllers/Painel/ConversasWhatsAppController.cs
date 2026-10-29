using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Atendimento humano das conversas do WhatsApp (agente, Fase 4). Sempre no negócio do token — nenhuma rota recebe negócio, e conversa
/// de outro negócio, inexistente ou anonimizada responde 404 (igual). Lista, detalhe e resumo são só leitura; nenhum texto de conversa
/// vai para log.
/// </summary>
[ApiController]
[Route("painel/whatsapp/conversas")]
[Authorize(Policy = nameof(Permissao.AtenderConversasWhatsApp))]
public sealed class ConversasWhatsAppController : ControllerBase
{
    private readonly IGerenciadorConversasWhatsApp _conversas;
    private readonly IConsultaConversasWhatsApp _consulta;
    private readonly IServicoLgpdConversasWhatsApp _lgpd;

    public ConversasWhatsAppController(
        IGerenciadorConversasWhatsApp conversas, IConsultaConversasWhatsApp consulta, IServicoLgpdConversasWhatsApp lgpd)
    {
        _conversas = conversas;
        _consulta = consulta;
        _lgpd = lgpd;
    }

    /// <summary>
    /// Lista paginada (20 por página), mais recente primeiro, SEM texto de mensagem. <c>filtro</c>: <c>todas</c> (padrão),
    /// <c>aguardando</c> (em Humano e ninguém assumiu) ou <c>humano</c> (todas em atendimento humano).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginaConversasWhatsApp>> Listar(
        [FromQuery] string? filtro, [FromQuery] int pagina = 1, CancellationToken cancellationToken = default)
    {
        FiltroConversasWhatsApp valor;
        switch (filtro?.Trim().ToLowerInvariant())
        {
            case null or "" or "todas":
                valor = FiltroConversasWhatsApp.Todas;
                break;
            case "aguardando":
                valor = FiltroConversasWhatsApp.Aguardando;
                break;
            case "humano":
                valor = FiltroConversasWhatsApp.Humano;
                break;
            default:
                return BadRequest(new ProblemDetails
                {
                    Title = "Filtro inválido. Use todas, aguardando ou humano.",
                    Extensions = { ["codigo"] = "filtro_invalido" },
                });
        }

        return Ok(await _consulta.ListarAsync(valor, pagina, cancellationToken));
    }

    /// <summary>Contadores para o menu: conversas aguardando atendimento (ninguém assumiu) e todas em atendimento humano.</summary>
    [HttpGet("resumo")]
    public async Task<ActionResult<ResumoConversasWhatsApp>> Resumo(CancellationToken cancellationToken) =>
        Ok(await _consulta.ResumoAsync(cancellationToken));

    /// <summary>Até 50 mensagens em ordem cronológica. <c>antesDe</c> + <c>antesDeId</c> (o <c>em</c> e o <c>id</c> da mais antiga já exibida) carregam as anteriores.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DetalheConversaWhatsApp>> Obter(Guid id, [FromQuery] DateTimeOffset? antesDe, [FromQuery] Guid? antesDeId, CancellationToken cancellationToken)
    {
        var detalhe = await _consulta.ObterAsync(id, antesDe, antesDeId, cancellationToken);
        return detalhe is null ? NotFound() : Ok(detalhe);
    }

    /// <summary>
    /// Apaga as mensagens da conversa e anonimiza a linha (LGPD). Exige também <c>GerenciarClientes</c> (é a mesma natureza de
    /// excluir dados de cliente). Auditoria só com o id.
    /// </summary>
    [HttpPost("{id:guid}/apagar")]
    [Authorize(Policy = nameof(Permissao.GerenciarClientes))]
    public async Task<IActionResult> Apagar(Guid id, CancellationToken cancellationToken) =>
        await _lgpd.ApagarConversaAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>O agente fica em silêncio nesta conversa. Idempotente (204 também se já estava assumida).</summary>
    [HttpPost("{id:guid}/assumir")]
    public async Task<IActionResult> Assumir(Guid id, CancellationToken cancellationToken) =>
        await _conversas.AssumirAsync(id, cancellationToken) == ResultadoConversaWhatsApp.Ok ? NoContent() : NotFound();

    /// <summary>Devolve ao agente sem responder o que se acumulou. Idempotente (204 também se já estava com o agente).</summary>
    [HttpPost("{id:guid}/devolver")]
    public async Task<IActionResult> Devolver(Guid id, CancellationToken cancellationToken) =>
        await _conversas.DevolverAsync(id, cancellationToken) == ResultadoConversaWhatsApp.Ok ? NoContent() : NotFound();
}
