using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Comissoes;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// "Minha agenda" (seção 7): o Profissional logado vê a própria agenda (dia e semana) e, nos atendimentos dele, faz
/// Iniciar, Concluir e Faltou — sem precisar de "gerenciar agenda" (decisão do dono). Criar, remarcar e cancelar ficam
/// com quem gerencia a agenda. O profissional vem do token; atendimento de outro responde 404 (não revela que existe).
/// </summary>
[ApiController]
[Route("painel/minha-agenda")]
[Authorize]
public sealed class MinhaAgendaController : ControllerBase
{
    private readonly IAgendaDoProfissionalLogado _minha;
    private readonly IServicoAgendamentos _agendamentos;

    public MinhaAgendaController(IAgendaDoProfissionalLogado minha, IServicoAgendamentos agendamentos)
    {
        _minha = minha;
        _agendamentos = agendamentos;
    }

    /// <summary>Sem vínculo com profissional: 204.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgendamentoResumo>>> Dia([FromQuery] DateOnly data, CancellationToken cancellationToken) =>
        await _minha.MeuProfissionalAsync(cancellationToken) is { } profissionalId
            ? Ok(await _agendamentos.ListarAgendaDoDiaAsync(profissionalId, data, cancellationToken))
            : NoContent();

    [HttpGet("semana")]
    public async Task<ActionResult<AgendaSemana>> Semana(
        [FromQuery] DateOnly inicio, [FromServices] IConsultaAgendaSemana consulta, CancellationToken cancellationToken) =>
        await _minha.MeuProfissionalAsync(cancellationToken) is { } profissionalId
            && await consulta.ObterAsync(profissionalId, inicio, cancellationToken) is { } semana
            ? Ok(semana)
            : NoContent();

    [HttpPost("{id:guid}/iniciar")]
    public Task<IActionResult> Iniciar(Guid id, CancellationToken cancellationToken) =>
        NoMeuAsync(id, () => _agendamentos.IniciarAtendimentoAsync(id, cancellationToken), cancellationToken);

    [HttpPost("{id:guid}/concluir")]
    public Task<IActionResult> Concluir(Guid id, CancellationToken cancellationToken) =>
        NoMeuAsync(id, () => _agendamentos.MarcarConcluidoAsync(id, cancellationToken), cancellationToken);

    [HttpPost("{id:guid}/faltou")]
    public Task<IActionResult> Faltou(Guid id, CancellationToken cancellationToken) =>
        NoMeuAsync(id, () => _agendamentos.MarcarFaltouAsync(id, cancellationToken), cancellationToken);

    private async Task<IActionResult> NoMeuAsync(Guid id, Func<Task<bool>> mudanca, CancellationToken cancellationToken)
    {
        if (!await _minha.EhMeuAsync(id, cancellationToken))
            return NotFound();

        try
        {
            return await mudanca() ? NoContent() : NotFound();
        }
        catch (InvalidOperationException excecao) when (excecao is not QuinzenaFechadaException)
        {
            // Transição que não vale para o status atual é 409; quinzena fechada segue para o middleware (codigo "quinzena_fechada").
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: excecao.Message);
        }
    }
}
