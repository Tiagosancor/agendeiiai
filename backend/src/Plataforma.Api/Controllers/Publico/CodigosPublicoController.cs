using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plataforma.Api.Assinaturas;
using Plataforma.Aplicacao.Verificacao;
using Plataforma.Dominio.Comum;

namespace Plataforma.Api.Controllers.Publico;

/// <summary>Código de confirmação do cliente final (seção 8.1). Anti-enumeração: a resposta de <see cref="Solicitar"/> nunca depende do telefone já ser cliente ou não.</summary>
[ApiController]
[Route("publico/codigos")]
[EnableRateLimiting("CodigoVerificacaoPorIp")]
public sealed class CodigosPublicoController : ControllerBase
{
    private readonly IServicoVerificacao _servico;

    public CodigosPublicoController(IServicoVerificacao servico)
    {
        _servico = servico;
    }

    public sealed record SolicitarCodigoRequisicao(string Telefone, string? Email);

    [HttpPost]
    [BloquearComAssinaturaSuspensa]
    public async Task<IActionResult> Solicitar(SolicitarCodigoRequisicao requisicao, CancellationToken cancellationToken)
    {
        if (!TelefoneE164.TentarCriar(requisicao.Telefone, out var telefone))
            return BadRequest(new ProblemDetails { Title = "Telefone inválido." });

        var resultado = await _servico.SolicitarCodigoAsync(telefone!, requisicao.Email, cancellationToken);

        if (resultado.LimiteExcedido)
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails { Title = "Muitos pedidos de código. Tente novamente mais tarde." });

        // Resposta idêntica exista ou não o telefone no cadastro (seção 8.1.3).
        return Accepted();
    }

    /// <summary>
    /// Mesmo texto para qualquer motivo interno (WhatsApp fora, número sem WhatsApp, e-mail
    /// devolvido): a resposta nunca diz qual canal falhou, para não vazar nada sobre o número.
    /// </summary>
    public const string MensagemLimiteReenvios =
        "Você já pediu o código várias vezes. Use o último código que enviamos — confira também o seu e-mail.";

    /// <summary>
    /// Reenvia o código (seção 8.1): troca o código em vigor e manda de novo pelos dois canais,
    /// até o limite de reenvios. Passa pelo mesmo rate limit por IP do pedido original.
    /// </summary>
    [HttpPost("reenviar")]
    [BloquearComAssinaturaSuspensa]
    public async Task<IActionResult> Reenviar(SolicitarCodigoRequisicao requisicao, CancellationToken cancellationToken)
    {
        if (!TelefoneE164.TentarCriar(requisicao.Telefone, out var telefone))
            return BadRequest(new ProblemDetails { Title = "Telefone inválido." });

        var resultado = await _servico.ReenviarCodigoAsync(telefone!, requisicao.Email, cancellationToken);

        if (resultado.LimiteReenviosAtingido)
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails { Title = MensagemLimiteReenvios });

        if (resultado.LimiteExcedido)
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails { Title = "Muitos pedidos de código. Tente novamente mais tarde." });

        return Accepted();
    }

    public sealed record ValidarCodigoRequisicao(string Telefone, string Codigo);

    [HttpPost("validar")]
    public async Task<IActionResult> Validar(ValidarCodigoRequisicao requisicao, CancellationToken cancellationToken)
    {
        if (!TelefoneE164.TentarCriar(requisicao.Telefone, out var telefone))
            return BadRequest(new ProblemDetails { Title = "Telefone inválido." });

        var resultado = await _servico.ValidarCodigoAsync(telefone!, requisicao.Codigo, cancellationToken);

        return resultado.Sucesso
            ? Ok(new { tokenVerificacao = resultado.TokenVerificacao })
            : BadRequest(new ProblemDetails { Title = resultado.MensagemErro });
    }
}
