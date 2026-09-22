using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
