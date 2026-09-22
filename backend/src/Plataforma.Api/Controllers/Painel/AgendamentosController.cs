using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Criação e gestão de agendamentos pelo painel (seção 11, Sprint 2 — "encaixe manual"):
/// aceita qualquer horário válido, não só os sugeridos por <see cref="DisponibilidadeController"/>.
/// </summary>
[ApiController]
[Route("painel/agendamentos")]
[Authorize(Policy = nameof(Permissao.GerenciarAgenda))]
public sealed class AgendamentosController : ControllerBase
{
    private readonly IServicoAgendamentos _servicoAgendamentos;

    public AgendamentosController(IServicoAgendamentos servicoAgendamentos)
    {
        _servicoAgendamentos = servicoAgendamentos;
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> Criar(CriarAgendamento dados, CancellationToken cancellationToken)
    {
        var resultado = await _servicoAgendamentos.CriarAsync(dados, cancellationToken);
        return TraduzirResultado(resultado);
    }

    /// <summary>Reserva temporária de 10 min (seção 8.2.2) — mecanismo do assistente público (Sprint 3), exposto aqui pra ser testável desde já.</summary>
    [HttpPost("reserva")]
    public async Task<ActionResult<Guid>> CriarReserva(CriarAgendamento dados, CancellationToken cancellationToken)
    {
        var resultado = await _servicoAgendamentos.CriarReservaAsync(dados, cancellationToken);
        return TraduzirResultado(resultado);
    }

    [HttpPost("{id:guid}/confirmar")]
    public async Task<IActionResult> ConfirmarReserva(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await _servicoAgendamentos.ConfirmarReservaAsync(id, cancellationToken);
        return resultado.Sucesso ? NoContent() : BadRequest(new ProblemDetails { Title = resultado.MensagemErro });
    }

    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken cancellationToken) =>
        await _servicoAgendamentos.CancelarAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPut("{id:guid}/mover")]
    public async Task<IActionResult> Mover(Guid id, MoverAgendamentoRequisicao dados, CancellationToken cancellationToken)
    {
        var resultado = await _servicoAgendamentos.MoverAsync(id, dados.NovoInicio, cancellationToken);
        return resultado.Sucesso ? NoContent() : TraduzirErro(resultado);
    }

    [HttpPost("{id:guid}/concluir")]
    public async Task<IActionResult> MarcarConcluido(Guid id, CancellationToken cancellationToken) =>
        await _servicoAgendamentos.MarcarConcluidoAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/faltou")]
    public async Task<IActionResult> MarcarFaltou(Guid id, CancellationToken cancellationToken) =>
        await _servicoAgendamentos.MarcarFaltouAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpGet("~/painel/agenda")]
    public async Task<ActionResult<IReadOnlyList<AgendamentoResumo>>> ListarAgendaDoDia(
        [FromQuery] Guid profissionalId, [FromQuery] DateOnly data, CancellationToken cancellationToken) =>
        Ok(await _servicoAgendamentos.ListarAgendaDoDiaAsync(profissionalId, data, cancellationToken));

    private ActionResult<Guid> TraduzirResultado(ResultadoAgendamento resultado) =>
        resultado.Sucesso
            ? StatusCode(StatusCodes.Status201Created, resultado.AgendamentoId)
            : TraduzirErro(resultado);

    /// <summary>Nunca 500 nem detalhe de banco (seção 8.2.4) — conflito de horário vira 409 amigável com sugestões; o resto, 400.</summary>
    private ObjectResult TraduzirErro(ResultadoAgendamento resultado)
    {
        if (resultado.Conflito)
        {
            return Conflict(new
            {
                title = resultado.MensagemErro,
                proximosHorariosLivres = resultado.ProximosHorariosLivres,
            });
        }

        return BadRequest(new ProblemDetails { Title = resultado.MensagemErro });
    }
}

public sealed record MoverAgendamentoRequisicao(DateTimeOffset NovoInicio);
