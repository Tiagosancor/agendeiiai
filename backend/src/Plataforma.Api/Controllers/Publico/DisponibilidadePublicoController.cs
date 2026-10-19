using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Publico;

namespace Plataforma.Api.Controllers.Publico;

public sealed record HorarioLivrePublico(DateTimeOffset Inicio, Guid ProfissionalId);

/// <summary>
/// Horários livres do assistente público (seção 6.2.2). Com <c>profissionalId</c>, é a
/// disponibilidade de um só; sem ele ("Qualquer profissional", seção 6.2.1), agrega todos
/// os profissionais ativos que executam TODOS os serviços escolhidos (vínculo via
/// <c>ProfissionalServico</c> — Sprint 2). A duração é a de cada profissional (duração
/// personalizada do vínculo, quando há — a mesma que a reserva grava); <c>duracaoMinutos</c> só
/// vale para quem não tem vínculo com os serviços pedidos. A regra mora em
/// <see cref="IConsultaHorariosPublicos"/>, compartilhada com o agente de WhatsApp.
/// </summary>
[ApiController]
[Route("publico/horarios-livres")]
public sealed class DisponibilidadePublicoController : ControllerBase
{
    private const int MaximoHorariosRetornados = 60;

    private readonly IConsultaHorariosPublicos _consultaHorarios;

    public DisponibilidadePublicoController(IConsultaHorariosPublicos consultaHorarios) => _consultaHorarios = consultaHorarios;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HorarioLivrePublico>>> Listar(
        [FromQuery] DateOnly data, [FromQuery] int duracaoMinutos, [FromQuery] Guid[] servicoIds,
        [FromQuery] Guid? profissionalId, CancellationToken cancellationToken)
    {
        var horarios = await _consultaHorarios.ListarAsync(data, duracaoMinutos, servicoIds, profissionalId, MaximoHorariosRetornados, cancellationToken);
        return Ok(horarios.Select(h => new HorarioLivrePublico(h.Inicio, h.ProfissionalId)).ToList());
    }
}
