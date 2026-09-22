using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Comum;

namespace Plataforma.Api.Controllers.Publico;

/// <summary>
/// Reserva e confirmação do assistente público (seção 6.2/8.2). A reserva (10 min) é
/// criada ao escolher o horário, sem cliente ainda; a confirmação só acontece com o
/// <c>tokenVerificacao</c> válido (seção 8.1.1.d) — sem ele, ou de outro telefone/negócio, 401.
/// </summary>
[ApiController]
[Route("publico")]
public sealed class AgendamentosPublicoController : ControllerBase
{
    private readonly IServicoAgendamentos _servicoAgendamentos;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly IContextoNegocio _contextoNegocio;

    public AgendamentosPublicoController(
        IServicoAgendamentos servicoAgendamentos, IServicoTokenPublico servicoToken, IContextoNegocio contextoNegocio)
    {
        _servicoAgendamentos = servicoAgendamentos;
        _servicoToken = servicoToken;
        _contextoNegocio = contextoNegocio;
    }

    public sealed record CriarReservaRequisicao(Guid ProfissionalId, IReadOnlyList<Guid> ServicoIds, DateTimeOffset Inicio);

    [HttpPost("reservas")]
    public async Task<IActionResult> CriarReserva(CriarReservaRequisicao requisicao, CancellationToken cancellationToken)
    {
        var resultado = await _servicoAgendamentos.CriarReservaPublicaAsync(
            new CriarReservaPublica(requisicao.ProfissionalId, requisicao.ServicoIds, requisicao.Inicio), cancellationToken);

        return TraduzirResultado(resultado, sucesso: () => StatusCode(StatusCodes.Status201Created, new { agendamentoId = resultado.AgendamentoId }));
    }

    public sealed record ValidarCupomRequisicao(Guid AgendamentoId, string Codigo);

    [HttpPost("cupons/validar")]
    public async Task<IActionResult> ValidarCupom(ValidarCupomRequisicao requisicao, CancellationToken cancellationToken)
    {
        var resultado = await _servicoAgendamentos.PreVisualizarCupomAsync(requisicao.AgendamentoId, requisicao.Codigo, cancellationToken);

        return resultado.Sucesso
            ? Ok(new { desconto = resultado.Desconto })
            : BadRequest(new ProblemDetails { Title = resultado.MensagemErro });
    }

    public sealed record ConfirmarAgendamentoRequisicao(
        Guid AgendamentoId, string TokenVerificacao, string Nome, string Telefone, string? Email,
        string? Observacoes, string? CodigoCupom);

    /// <summary>Confirmação final (seção 8.1.1.d) — o agendamento só existe de verdade depois desta chamada, com o código validado.</summary>
    [HttpPost("agendamentos")]
    public async Task<IActionResult> Confirmar(ConfirmarAgendamentoRequisicao requisicao, CancellationToken cancellationToken)
    {
        if (!TelefoneE164.TentarCriar(requisicao.Telefone, out var telefone))
            return BadRequest(new ProblemDetails { Title = "Telefone inválido." });

        var negocioId = _contextoNegocio.NegocioId!.Value;

        if (string.IsNullOrWhiteSpace(requisicao.TokenVerificacao)
            || !await _servicoToken.ValidarTokenVerificacaoAsync(requisicao.TokenVerificacao, negocioId, telefone!))
        {
            return Unauthorized(new ProblemDetails { Title = "Verificação por código inválida ou expirada. Solicite um novo código." });
        }

        var resultado = await _servicoAgendamentos.ConfirmarReservaPublicaAsync(
            new ConfirmarReservaPublica(
                requisicao.AgendamentoId, requisicao.Nome, telefone!.Valor, requisicao.Email, requisicao.Observacoes,
                requisicao.CodigoCupom, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido"),
            cancellationToken);

        // O token de cancelar/remarcar (seção 6.3) vai junto na resposta — a tela de sucesso
        // do assistente mostra os links na hora, sem depender do cliente abrir o e-mail.
        return TraduzirResultado(resultado, sucesso: () => StatusCode(StatusCodes.Status201Created, new
        {
            agendamentoId = resultado.AgendamentoId,
            tokenAgendamento = _servicoToken.GerarTokenAgendamento(negocioId, resultado.AgendamentoId!.Value),
        }));
    }

    private IActionResult TraduzirResultado(ResultadoAgendamento resultado, Func<IActionResult> sucesso)
    {
        if (resultado.Sucesso)
            return sucesso();

        if (resultado.Conflito)
            return Conflict(new { title = resultado.MensagemErro, proximosHorariosLivres = resultado.ProximosHorariosLivres });

        return BadRequest(new ProblemDetails { Title = resultado.MensagemErro });
    }
}
