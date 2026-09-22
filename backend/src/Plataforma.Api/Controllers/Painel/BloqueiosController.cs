using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Authorize(Policy = nameof(Permissao.GerenciarProfissionais))]
public sealed class BloqueiosController : ControllerBase
{
    private readonly IGerenciadorBloqueios _gerenciador;

    public BloqueiosController(IGerenciadorBloqueios gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet("painel/profissionais/{profissionalId:guid}/bloqueios")]
    public async Task<ActionResult<IReadOnlyList<BloqueioResumo>>> Listar(Guid profissionalId, CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ListarAsync(profissionalId, cancellationToken));

    [HttpPost("painel/profissionais/{profissionalId:guid}/bloqueios")]
    public async Task<ActionResult<Guid>> Criar(Guid profissionalId, CriarBloqueioRequisicao dados, CancellationToken cancellationToken)
    {
        var id = await _gerenciador.CriarAsync(profissionalId, dados.InicioUtc, dados.FimUtc, dados.Motivo, cancellationToken);
        return CreatedAtAction(nameof(Listar), new { profissionalId }, id);
    }

    [HttpDelete("painel/bloqueios/{id:guid}")]
    public async Task<IActionResult> Remover(Guid id, CancellationToken cancellationToken) =>
        await _gerenciador.RemoverAsync(id, cancellationToken) ? NoContent() : NotFound();
}

public sealed record CriarBloqueioRequisicao(DateTimeOffset InicioUtc, DateTimeOffset FimUtc, string? Motivo);
