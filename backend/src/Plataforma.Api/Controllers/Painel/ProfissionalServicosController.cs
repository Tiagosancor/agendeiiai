using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Route("painel/profissionais/{profissionalId:guid}/servicos")]
[Authorize(Policy = nameof(Permissao.GerenciarProfissionais))]
public sealed class ProfissionalServicosController : ControllerBase
{
    private readonly IGerenciadorProfissionalServicos _gerenciador;

    public ProfissionalServicosController(IGerenciadorProfissionalServicos gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProfissionalServicoResumo>>> Listar(
        Guid profissionalId, CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ListarAsync(profissionalId, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Vincular(Guid profissionalId, VincularServicoRequisicao dados, CancellationToken cancellationToken)
    {
        await _gerenciador.VincularAsync(
            profissionalId, dados.ServicoId, dados.PrecoPersonalizado, dados.DuracaoPersonalizadaMinutos, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{servicoId:guid}")]
    public async Task<IActionResult> Desvincular(Guid profissionalId, Guid servicoId, CancellationToken cancellationToken) =>
        await _gerenciador.DesvincularAsync(profissionalId, servicoId, cancellationToken) ? NoContent() : NotFound();
}

public sealed record VincularServicoRequisicao(Guid ServicoId, decimal? PrecoPersonalizado, int? DuracaoPersonalizadaMinutos);
