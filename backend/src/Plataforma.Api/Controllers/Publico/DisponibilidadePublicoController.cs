using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Publico;

namespace Plataforma.Api.Controllers.Publico;

public sealed record HorarioLivrePublico(DateTimeOffset Inicio, Guid ProfissionalId);

/// <summary>
/// Horários livres do assistente público (seção 6.2.2). Com <c>profissionalId</c>, é a
/// disponibilidade de um só; sem ele ("Qualquer profissional", seção 6.2.1), agrega todos
/// os profissionais ativos que executam TODOS os serviços escolhidos (vínculo via
/// <c>ProfissionalServico</c> — Sprint 2).
/// </summary>
[ApiController]
[Route("publico/horarios-livres")]
public sealed class DisponibilidadePublicoController : ControllerBase
{
    private const int MaximoHorariosRetornados = 60;

    private readonly IConsultaDisponibilidade _consultaDisponibilidade;
    private readonly IConsultaCatalogoPublico _consultaCatalogo;

    public DisponibilidadePublicoController(
        IConsultaDisponibilidade consultaDisponibilidade, IConsultaCatalogoPublico consultaCatalogo)
    {
        _consultaDisponibilidade = consultaDisponibilidade;
        _consultaCatalogo = consultaCatalogo;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HorarioLivrePublico>>> Listar(
        [FromQuery] DateOnly data, [FromQuery] int duracaoMinutos, [FromQuery] Guid[] servicoIds,
        [FromQuery] Guid? profissionalId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> profissionalIds;

        if (profissionalId is not null)
        {
            profissionalIds = [profissionalId.Value];
        }
        else
        {
            var profissionais = await _consultaCatalogo.ListarProfissionaisAsync(cancellationToken);
            profissionalIds = profissionais
                .Where(p => servicoIds.All(id => p.ServicoIds.Contains(id)))
                .Select(p => p.Id)
                .ToList();
        }

        var horarios = new List<HorarioLivrePublico>();

        foreach (var id in profissionalIds)
        {
            var livres = await _consultaDisponibilidade.ListarHorariosLivresAsync(id, data, duracaoMinutos, cancellationToken);
            horarios.AddRange(livres.Select(h => new HorarioLivrePublico(h, id)));
        }

        return Ok(horarios.OrderBy(h => h.Inicio).Take(MaximoHorariosRetornados).ToList());
    }
}
