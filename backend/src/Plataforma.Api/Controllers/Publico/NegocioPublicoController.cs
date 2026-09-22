using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Negocios;

namespace Plataforma.Api.Controllers.Publico;

/// <summary>
/// Endpoints públicos e anônimos relacionados ao negócio resolvido pelo subdomínio.
/// Nenhum dado pessoal de terceiros passa por aqui (seção 8.1.3) — só o recorte
/// público do negócio em si.
/// </summary>
[ApiController]
[Route("publico/negocio")]
public sealed class NegocioPublicoController : ControllerBase
{
    private readonly IConsultaNegocioPublico _consultaNegocio;

    public NegocioPublicoController(IConsultaNegocioPublico consultaNegocio)
    {
        _consultaNegocio = consultaNegocio;
    }

    /// <summary>
    /// Devolve o negócio resolvido pelo host da requisição pelo
    /// <c>ResolucaoNegocioMiddleware</c>. Se o middleware não resolveu nenhum negócio,
    /// a requisição já teria recebido 404 antes de chegar aqui (rota sob /publico).
    /// </summary>
    [HttpGet]
    public ActionResult<NegocioResumo> ObterNegocioAtual()
    {
        if (HttpContext.Items[nameof(NegocioResumo)] is not NegocioResumo negocio)
            return NotFound();

        return Ok(negocio);
    }

    /// <summary>
    /// Resolução servidor-a-servidor por slug explícito — usada pelo middleware do
    /// Next.js para decidir, antes de renderizar, se o subdomínio corresponde a um
    /// negócio ativo (seção 8.3.3). Só devolve informação pública, então receber o
    /// slug por rota (em vez de pelo host) não fere a regra da seção 8.3.2, que é
    /// sobre nunca confiar em entrada do cliente para dado do painel.
    /// </summary>
    [HttpGet("/publico/negocios-por-slug/{slug}")]
    public async Task<ActionResult<NegocioResumo>> ObterPorSlug(
        string slug, CancellationToken cancellationToken)
    {
        if (!Slug.TentarCriar(slug, out var slugValido))
            return NotFound();

        var negocio = await _consultaNegocio.ObterPorSlugAsync(slugValido!, cancellationToken);

        return negocio is null ? NotFound() : Ok(negocio);
    }
}
