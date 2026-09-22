using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Route("painel/profissionais/{profissionalId:guid}/horarios")]
[Authorize(Policy = nameof(Permissao.GerenciarProfissionais))]
public sealed class HorariosTrabalhoController : ControllerBase
{
    private readonly IGerenciadorHorariosTrabalho _gerenciador;

    public HorariosTrabalhoController(IGerenciadorHorariosTrabalho gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IntervaloTrabalho>>> Listar(Guid profissionalId, CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ListarAsync(profissionalId, cancellationToken));

    /// <summary>Substitui o horário de trabalho inteiro do profissional (todos editáveis — seção 7). Um dia com almoço vira dois intervalos.</summary>
    [HttpPut]
    public async Task<IActionResult> Definir(
        Guid profissionalId, IReadOnlyList<IntervaloTrabalho> intervalos, CancellationToken cancellationToken)
    {
        await _gerenciador.DefinirAsync(profissionalId, intervalos, cancellationToken);
        return NoContent();
    }
}
