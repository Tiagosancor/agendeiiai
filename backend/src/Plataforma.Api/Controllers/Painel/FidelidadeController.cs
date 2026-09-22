using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Fidelidade;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Route("painel/fidelidade")]
[Authorize(Policy = nameof(Permissao.GerenciarFidelidade))]
public sealed class FidelidadeController : ControllerBase
{
    private readonly IGerenciadorFidelidade _gerenciador;

    public FidelidadeController(IGerenciadorFidelidade gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet]
    public async Task<ActionResult<ProgramaFidelidadeResumo>> ObterPrograma(CancellationToken cancellationToken)
    {
        var programa = await _gerenciador.ObterProgramaAsync(cancellationToken);
        return programa is null ? NotFound() : Ok(programa);
    }

    [HttpPut]
    public async Task<IActionResult> DefinirPrograma(DefinirProgramaFidelidade dados, CancellationToken cancellationToken)
    {
        await _gerenciador.DefinirProgramaAsync(dados, cancellationToken);
        return NoContent();
    }

    [HttpGet("clientes/{clienteId:guid}/progresso")]
    public async Task<ActionResult<ProgressoFidelidade>> ObterProgresso(Guid clienteId, CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ObterProgressoAsync(clienteId, cancellationToken));

    [HttpPost("clientes/{clienteId:guid}/resgatar")]
    public async Task<IActionResult> Resgatar(Guid clienteId, CancellationToken cancellationToken) =>
        await _gerenciador.ResgatarRecompensaAsync(clienteId, cancellationToken)
            ? NoContent()
            : BadRequest(new ProblemDetails { Title = "Esse cliente ainda não tem selos suficientes para resgatar." });
}
