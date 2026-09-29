using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Forçar agendamento (seção 7): criar ou mover por cima das regras de horário. Existe só aqui, no painel,
/// sob a permissão própria (desligada para todos, exceto o Administrador). Nenhum endpoint público recebe
/// motivo nem grava <c>Forcado</c>. Assinatura suspensa barra antes, pelo filtro do painel (402).
/// </summary>
[ApiController]
[Route("painel/agendamentos/forcados")]
[Authorize(Policy = nameof(Permissao.ForcarAgendamento))]
public sealed class AgendamentosForcadosController : ControllerBase
{
    private readonly IServicoAgendamentos _servicoAgendamentos;

    public AgendamentosForcadosController(IServicoAgendamentos servicoAgendamentos) => _servicoAgendamentos = servicoAgendamentos;

    /// <summary>O aviso antes do "Forçar mesmo assim": quais regras serão quebradas, ou por que não dá para forçar.</summary>
    [HttpPost("previa")]
    public async Task<ActionResult<PreviaForcar>> Previa(ConsultaForcar dados, CancellationToken cancellationToken) =>
        Ok(await _servicoAgendamentos.PreverForcarAsync(dados, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Criar(CriarAgendamentoForcado dados, CancellationToken cancellationToken)
    {
        var resultado = await _servicoAgendamentos.CriarForcadoAsync(
            new CriarAgendamento(dados.ProfissionalId, dados.ClienteId, dados.ServicoIds, dados.Inicio, dados.Observacoes),
            dados.Motivo ?? string.Empty, cancellationToken);

        return resultado.Sucesso
            ? StatusCode(StatusCodes.Status201Created, resultado.AgendamentoId)
            : Traduzir(resultado);
    }

    [HttpPut("{id:guid}/mover")]
    public async Task<IActionResult> Mover(Guid id, MoverAgendamentoForcado dados, CancellationToken cancellationToken)
    {
        var resultado = await _servicoAgendamentos.MoverForcadoAsync(id, dados.NovoInicio, dados.Motivo ?? string.Empty, cancellationToken);
        return resultado.Sucesso ? NoContent() : Traduzir(resultado);
    }

    private ObjectResult Traduzir(ResultadoAgendamento resultado) =>
        resultado.Conflito
            ? Conflict(new { title = resultado.MensagemErro, proximosHorariosLivres = resultado.ProximosHorariosLivres })
            : BadRequest(new ProblemDetails { Title = resultado.MensagemErro });
}

public sealed record CriarAgendamentoForcado(
    Guid ProfissionalId, Guid? ClienteId, IReadOnlyList<Guid> ServicoIds, DateTimeOffset Inicio, string? Observacoes, string? Motivo);

public sealed record MoverAgendamentoForcado(DateTimeOffset NovoInicio, string? Motivo);
