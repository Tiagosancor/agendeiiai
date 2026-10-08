using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Ics;

namespace Plataforma.Api.Controllers.Publico;

/// <summary>
/// Cancelar/remarcar por link seguro (seção 6.3) — o token (JWT, seção 4) identifica o
/// agendamento e o negócio; nenhum id de agendamento é aceito diretamente na URL sem ele.
/// O servidor decide o que é permitido (<c>acoes</c> no detalhe) e os POSTs revalidam a mesma decisão.
/// Respostas por token têm dado pessoal: nunca ficam em cache de navegador, proxy ou CDN.
/// </summary>
[ApiController]
[Route("publico/meus-agendamentos/{token}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MeusAgendamentosController : ControllerBase
{
    private readonly IServicoAgendamentos _servicoAgendamentos;
    private readonly IGestaoPublicaAgendamento _gestao;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IGeradorIcs _geradorIcs;

    public MeusAgendamentosController(
        IServicoAgendamentos servicoAgendamentos, IGestaoPublicaAgendamento gestao, IServicoTokenPublico servicoToken,
        IContextoNegocio contextoNegocio, IGeradorIcs geradorIcs)
    {
        _servicoAgendamentos = servicoAgendamentos;
        _gestao = gestao;
        _servicoToken = servicoToken;
        _contextoNegocio = contextoNegocio;
        _geradorIcs = geradorIcs;
    }

    [HttpGet]
    public async Task<IActionResult> Obter(string token, CancellationToken cancellationToken)
    {
        var (agendamentoId, erro) = await AutorizarAsync(token);
        if (agendamentoId is null)
            return erro!;

        var detalhe = await _gestao.ObterAsync(agendamentoId.Value, cancellationToken);
        return detalhe is null ? NaoEncontrado() : Ok(detalhe);
    }

    /// <summary>
    /// Horários livres para remarcar este agendamento: profissional, serviços e duração vêm do próprio agendamento
    /// (o token é o único contexto aceito) e o horário atual dele não conta como ocupado. O dia (<c>data</c>,
    /// yyyy-MM-dd) é o dia local no fuso do estabelecimento; os horários saem em UTC.
    /// </summary>
    [HttpGet("horarios-livres")]
    public async Task<IActionResult> ListarHorariosLivres(string token, [FromQuery] DateOnly? data, CancellationToken cancellationToken)
    {
        var (agendamentoId, erro) = await AutorizarAsync(token);
        if (agendamentoId is null)
            return erro!;

        if (data is null)
            return Problema(StatusCodes.Status400BadRequest, "data_invalida", "Informe a data (yyyy-MM-dd).");

        var resultado = await _gestao.ListarHorariosParaRemarcacaoAsync(agendamentoId.Value, data.Value, cancellationToken);
        if (resultado is null)
            return NaoEncontrado();

        return resultado.Bloqueio is { } bloqueio ? AcaoNaoPermitida(bloqueio) : Ok(resultado.Horarios);
    }

    [HttpPost("cancelar")]
    public async Task<IActionResult> Cancelar(string token, CancellationToken cancellationToken)
    {
        var (agendamentoId, erro) = await AutorizarAsync(token);
        if (agendamentoId is null)
            return erro!;

        var detalhe = await _gestao.ObterAsync(agendamentoId.Value, cancellationToken);
        if (detalhe is null)
            return NaoEncontrado();

        if (!detalhe.Acoes.Cancelar.Permitido)
            return AcaoNaoPermitida(detalhe.Acoes.Cancelar);

        try
        {
            if (!await _servicoAgendamentos.CancelarAsync(agendamentoId.Value, cancellationToken))
                return NaoEncontrado();
        }
        catch (InvalidOperationException)
        {
            // O status mudou entre a decisão e a gravação (ex.: o salão concluiu o atendimento agora).
            return AcaoNaoPermitida(AcaoGestaoPublica.Bloqueada(MotivosGestaoPublica.StatusNaoPermite, "Este agendamento não pode mais ser cancelado."));
        }

        return await SucessoAsync(agendamentoId.Value, cancellationToken);
    }

    public sealed record RemarcarRequisicao(DateTimeOffset NovoInicio);

    /// <summary><c>novoInicio</c> é um instante (qualquer offset é aceito e normalizado); a conferência é sempre refeita aqui.</summary>
    [HttpPost("remarcar")]
    public async Task<IActionResult> Remarcar(string token, RemarcarRequisicao requisicao, CancellationToken cancellationToken)
    {
        var (agendamentoId, erro) = await AutorizarAsync(token);
        if (agendamentoId is null)
            return erro!;

        var detalhe = await _gestao.ObterAsync(agendamentoId.Value, cancellationToken);
        if (detalhe is null)
            return NaoEncontrado();

        if (!detalhe.Acoes.Remarcar.Permitido)
            return AcaoNaoPermitida(detalhe.Acoes.Remarcar);

        ResultadoAgendamento resultado;
        try
        {
            resultado = await _servicoAgendamentos.MoverAsync(agendamentoId.Value, requisicao.NovoInicio, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return AcaoNaoPermitida(AcaoGestaoPublica.Bloqueada(MotivosGestaoPublica.StatusNaoPermite, "Este agendamento não pode mais ser remarcado."));
        }

        if (resultado.Conflito)
        {
            var problema = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = resultado.MensagemErro };
            problema.Extensions["codigo"] = MotivosGestaoPublica.HorarioIndisponivel;
            problema.Extensions["proximosHorariosLivres"] = resultado.ProximosHorariosLivres;
            return new ObjectResult(problema) { StatusCode = StatusCodes.Status409Conflict };
        }

        return resultado.Sucesso
            ? await SucessoAsync(agendamentoId.Value, cancellationToken)
            : Problema(StatusCodes.Status400BadRequest, "horario_invalido", resultado.MensagemErro ?? "Horário inválido.");
    }

    [HttpGet("ics")]
    public async Task<IActionResult> BaixarIcs(string token, CancellationToken cancellationToken)
    {
        var (agendamentoId, erro) = await AutorizarAsync(token);
        if (agendamentoId is null)
            return erro!;

        var detalhe = await _servicoAgendamentos.ObterDetalhePublicoAsync(agendamentoId.Value, cancellationToken);
        if (detalhe is null)
            return NaoEncontrado();

        var conteudo = _geradorIcs.Gerar(new DadosIcs(
            detalhe.Id, detalhe.NomeNegocio, detalhe.Local, detalhe.Inicio, detalhe.Fim,
            string.Join(", ", detalhe.Servicos)));

        return File(System.Text.Encoding.UTF8.GetBytes(conteudo), "text/calendar", "agendamento.ics");
    }

    /// <summary>
    /// Sem cabeçalho, 204 como sempre (quem já consome não muda). Com <c>Prefer: return=representation</c>, 200 com o
    /// detalhe canônico já atualizado — evita o GET extra depois do POST.
    /// </summary>
    private async Task<IActionResult> SucessoAsync(Guid agendamentoId, CancellationToken cancellationToken)
    {
        var quer = Request.Headers["Prefer"].Any(v => v is not null && v.Contains("return=representation", StringComparison.OrdinalIgnoreCase));
        if (!quer)
            return NoContent();

        var detalhe = await _gestao.ObterAsync(agendamentoId, cancellationToken);
        if (detalhe is null)
            return NoContent();

        Response.Headers["Preference-Applied"] = "return=representation";
        return Ok(detalhe);
    }

    /// <summary>Antecedência e horário passado seguem 400 (como sempre); o resto, 409 — nunca 500.</summary>
    private ObjectResult AcaoNaoPermitida(AcaoGestaoPublica acao)
    {
        var status = acao.CodigoMotivo is MotivosGestaoPublica.AntecedenciaMinima or MotivosGestaoPublica.AgendamentoPassado
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status409Conflict;
        return Problema(status, acao.CodigoMotivo ?? MotivosGestaoPublica.NaoPermitido, acao.Motivo ?? "Ação não permitida.");
    }

    private ObjectResult LinkInvalido() => Problema(StatusCodes.Status401Unauthorized, "link_invalido", "Link inválido ou expirado.");

    private ObjectResult NaoEncontrado() => Problema(StatusCodes.Status404NotFound, "nao_encontrado", "Agendamento não encontrado.");

    private static ObjectResult Problema(int status, string codigo, string titulo)
    {
        var problema = new ProblemDetails { Status = status, Title = titulo };
        problema.Extensions["codigo"] = codigo;
        return new ObjectResult(problema) { StatusCode = status };
    }

    /// <summary>Sem negócio resolvido é 404 (como slug inexistente); com negócio, token inválido é 401.</summary>
    private async Task<(Guid? AgendamentoId, IActionResult? Erro)> AutorizarAsync(string token)
    {
        if (_contextoNegocio.NegocioId is not { } negocioId)
            return (null, RespostasPublicas.NegocioNaoEncontrado(HttpContext));

        var agendamentoId = await _servicoToken.ValidarTokenAgendamentoAsync(token, negocioId);
        return agendamentoId is null ? (null, LinkInvalido()) : (agendamentoId, null);
    }
}
