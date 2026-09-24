using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Negocios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Route("painel/primeiros-passos")]
[Authorize]
public sealed class PrimeirosPassosController : ControllerBase
{
    private readonly IServicoPrimeirosPassos _servico;

    public PrimeirosPassosController(IServicoPrimeirosPassos servico) => _servico = servico;

    [HttpGet]
    public async Task<ActionResult<PrimeirosPassos>> Obter(CancellationToken cancellationToken) =>
        Ok(await _servico.ObterAsync(cancellationToken));

    [HttpPost("link-copiado")]
    public async Task<IActionResult> MarcarLinkCopiado(CancellationToken cancellationToken)
    {
        await _servico.MarcarLinkCopiadoAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("dispensar")]
    public async Task<IActionResult> Dispensar(CancellationToken cancellationToken)
    {
        await _servico.DispensarAsync(cancellationToken);
        return NoContent();
    }
}
