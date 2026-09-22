using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Publico;

namespace Plataforma.Api.Controllers.Publico;

/// <summary>Serviços e equipe da página pública (seção 6.1.3/6.1.4) — catálogo, nenhum dado pessoal de cliente (seção 8.1.3).</summary>
[ApiController]
[Route("publico")]
public sealed class CatalogoPublicoController : ControllerBase
{
    private readonly IConsultaCatalogoPublico _consulta;

    public CatalogoPublicoController(IConsultaCatalogoPublico consulta)
    {
        _consulta = consulta;
    }

    [HttpGet("servicos")]
    public async Task<ActionResult<IReadOnlyList<CategoriaComServicosPublicos>>> ListarServicos(CancellationToken cancellationToken) =>
        Ok(await _consulta.ListarServicosAsync(cancellationToken));

    [HttpGet("profissionais")]
    public async Task<ActionResult<IReadOnlyList<ProfissionalPublico>>> ListarProfissionais(CancellationToken cancellationToken) =>
        Ok(await _consulta.ListarProfissionaisAsync(cancellationToken));
}
