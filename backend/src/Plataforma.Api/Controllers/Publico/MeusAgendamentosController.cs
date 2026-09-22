using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Ics;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Api.Controllers.Publico;

/// <summary>
/// Cancelar/remarcar por link seguro (seção 6.3) — o token (JWT, seção 4) identifica o
/// agendamento e o negócio; nenhum id de agendamento é aceito diretamente na URL sem ele.
/// </summary>
[ApiController]
[Route("publico/meus-agendamentos/{token}")]
public sealed class MeusAgendamentosController : ControllerBase
{
    private readonly IServicoAgendamentos _servicoAgendamentos;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IGeradorIcs _geradorIcs;
    private readonly OpcoesAgendamentoPublico _opcoes;

    public MeusAgendamentosController(
        IServicoAgendamentos servicoAgendamentos, IServicoTokenPublico servicoToken, IContextoNegocio contextoNegocio,
        IGeradorIcs geradorIcs, IOptions<OpcoesAgendamentoPublico> opcoes)
    {
        _servicoAgendamentos = servicoAgendamentos;
        _servicoToken = servicoToken;
        _contextoNegocio = contextoNegocio;
        _geradorIcs = geradorIcs;
        _opcoes = opcoes.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(string token, CancellationToken cancellationToken)
    {
        var agendamentoId = await ValidarTokenAsync(token);
        if (agendamentoId is null)
            return Unauthorized(new ProblemDetails { Title = "Link inválido ou expirado." });

        var detalhe = await _servicoAgendamentos.ObterDetalhePublicoAsync(agendamentoId.Value, cancellationToken);
        return detalhe is null ? NotFound() : Ok(detalhe);
    }

    [HttpPost("cancelar")]
    public async Task<IActionResult> Cancelar(string token, CancellationToken cancellationToken)
    {
        var agendamentoId = await ValidarTokenAsync(token);
        if (agendamentoId is null)
            return Unauthorized(new ProblemDetails { Title = "Link inválido ou expirado." });

        var detalhe = await _servicoAgendamentos.ObterDetalhePublicoAsync(agendamentoId.Value, cancellationToken);
        if (detalhe is null)
            return NotFound();

        if (!RespeitaAntecedenciaMinima(detalhe.Inicio))
            return BadRequest(new ProblemDetails { Title = $"Cancele com pelo menos {_opcoes.AntecedenciaMinimaCancelamentoHoras}h de antecedência." });

        var cancelado = await _servicoAgendamentos.CancelarAsync(agendamentoId.Value, cancellationToken);
        return cancelado ? NoContent() : NotFound();
    }

    public sealed record RemarcarRequisicao(DateTimeOffset NovoInicio);

    [HttpPost("remarcar")]
    public async Task<IActionResult> Remarcar(string token, RemarcarRequisicao requisicao, CancellationToken cancellationToken)
    {
        var agendamentoId = await ValidarTokenAsync(token);
        if (agendamentoId is null)
            return Unauthorized(new ProblemDetails { Title = "Link inválido ou expirado." });

        var detalhe = await _servicoAgendamentos.ObterDetalhePublicoAsync(agendamentoId.Value, cancellationToken);
        if (detalhe is null)
            return NotFound();

        if (!RespeitaAntecedenciaMinima(detalhe.Inicio))
            return BadRequest(new ProblemDetails { Title = $"Remarque com pelo menos {_opcoes.AntecedenciaMinimaCancelamentoHoras}h de antecedência." });

        var resultado = await _servicoAgendamentos.MoverAsync(agendamentoId.Value, requisicao.NovoInicio, cancellationToken);

        if (resultado.Conflito)
            return Conflict(new { title = resultado.MensagemErro, proximosHorariosLivres = resultado.ProximosHorariosLivres });

        return resultado.Sucesso ? NoContent() : BadRequest(new ProblemDetails { Title = resultado.MensagemErro });
    }

    [HttpGet("ics")]
    public async Task<IActionResult> BaixarIcs(string token, CancellationToken cancellationToken)
    {
        var agendamentoId = await ValidarTokenAsync(token);
        if (agendamentoId is null)
            return Unauthorized(new ProblemDetails { Title = "Link inválido ou expirado." });

        var detalhe = await _servicoAgendamentos.ObterDetalhePublicoAsync(agendamentoId.Value, cancellationToken);
        if (detalhe is null)
            return NotFound();

        var conteudo = _geradorIcs.Gerar(new DadosIcs(
            detalhe.Id, detalhe.NomeNegocio, detalhe.Local, detalhe.Inicio, detalhe.Fim,
            string.Join(", ", detalhe.Servicos)));

        return File(System.Text.Encoding.UTF8.GetBytes(conteudo), "text/calendar", "agendamento.ics");
    }

    private bool RespeitaAntecedenciaMinima(DateTimeOffset inicio) =>
        inicio - DateTimeOffset.UtcNow >= TimeSpan.FromHours(_opcoes.AntecedenciaMinimaCancelamentoHoras);

    private async Task<Guid?> ValidarTokenAsync(string token) =>
        await _servicoToken.ValidarTokenAgendamentoAsync(token, _contextoNegocio.NegocioId!.Value);
}
