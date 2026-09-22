using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Route("painel/profissionais/{profissionalId:guid}/horarios-livres")]
[Authorize(Policy = nameof(Permissao.GerenciarAgenda))]
public sealed class DisponibilidadeController : ControllerBase
{
    private readonly IConsultaDisponibilidade _consultaDisponibilidade;

    public DisponibilidadeController(IConsultaDisponibilidade consultaDisponibilidade)
    {
        _consultaDisponibilidade = consultaDisponibilidade;
    }

    /// <summary>Horários livres na grade de 15 min (seção 6.2.2), considerando expediente, almoço, bloqueios e agendamentos já existentes.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DateTimeOffset>>> Listar(
        Guid profissionalId, [FromQuery] DateOnly data, [FromQuery] int duracaoMinutos, CancellationToken cancellationToken)
    {
        var horarios = await _consultaDisponibilidade.ListarHorariosLivresAsync(profissionalId, data, duracaoMinutos, cancellationToken);
        return Ok(horarios);
    }
}
