using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Cadastro;

namespace Plataforma.Api.Controllers.Cadastro;

/// <summary>
/// Cadastro de negócio novo, no domínio raiz (seção 6.5). Anônimo e sem tenant. Rate limit
/// por IP em tudo (seção 8.6.1), por e-mail no envio do código (dentro do serviço) e
/// captcha no envio do código e na criação da conta.
/// </summary>
[ApiController]
[Route("cadastro")]
public sealed class CadastroController : ControllerBase
{
    public const string PoliticaPorIp = "CadastroPorIp";
    public const string PoliticaSlugPorIp = "SlugPorIp";

    private readonly IServicoCadastro _servico;
    private readonly IVerificadorCaptcha _captcha;

    public CadastroController(IServicoCadastro servico, IVerificadorCaptcha captcha)
    {
        _servico = servico;
        _captcha = captcha;
    }

    /// <summary>Planos ativos para os cards do site e o passo 1 do cadastro — preços sempre do banco.</summary>
    [HttpGet("planos")]
    public async Task<ActionResult<IReadOnlyList<PlanoPublico>>> ListarPlanos(CancellationToken cancellationToken) =>
        Ok(await _servico.ListarPlanosAsync(cancellationToken));

    /// <summary>Conveniência da tela: o slug é revalidado no servidor na criação.</summary>
    [HttpGet("slugs/{slug}")]
    [EnableRateLimiting(PoliticaSlugPorIp)]
    public async Task<ActionResult<DisponibilidadeSlug>> VerificarSlug(string slug, CancellationToken cancellationToken) =>
        Ok(await _servico.VerificarSlugAsync(slug, cancellationToken));

    public sealed record SolicitarCodigoRequisicao(string Email, string? TokenCaptcha);

    [HttpPost("codigos")]
    [EnableRateLimiting(PoliticaPorIp)]
    public async Task<IActionResult> SolicitarCodigo(SolicitarCodigoRequisicao requisicao, CancellationToken cancellationToken)
    {
        if (!MailAddress.TryCreate(requisicao.Email, out _))
            return BadRequest(new ProblemDetails { Title = "E-mail inválido." });

        if (!await _captcha.ValidarAsync(requisicao.TokenCaptcha, Ip, cancellationToken))
            return BadRequest(new ProblemDetails { Title = "Confirme que você não é um robô e tente de novo." });

        var resultado = await _servico.SolicitarCodigoAsync(requisicao.Email, cancellationToken);

        if (resultado.LimiteExcedido)
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails { Title = "Muitos pedidos de código. Tente novamente mais tarde." });

        // Mesma resposta exista o e-mail ou não (seção 8.6.2).
        return Accepted();
    }

    public sealed record ValidarCodigoRequisicao(string Email, string Codigo);

    [HttpPost("codigos/validar")]
    [EnableRateLimiting(PoliticaPorIp)]
    public async Task<IActionResult> ValidarCodigo(ValidarCodigoRequisicao requisicao, CancellationToken cancellationToken)
    {
        var token = await _servico.ValidarCodigoAsync(requisicao.Email ?? string.Empty, requisicao.Codigo ?? string.Empty, cancellationToken);

        return token is null
            ? BadRequest(new ProblemDetails { Title = "Código inválido ou expirado." })
            : Ok(new { tokenCadastro = token });
    }

    public sealed record CadastrarRequisicao(
        string TokenCadastro, Guid PlanoId, string Periodicidade, string NomeNegocio, string TipoNegocio, string Slug,
        string Nome, string Email, string Telefone, string Senha, bool AceiteTermos, string? TokenCaptcha);

    /// <summary>Exige o cabeçalho <c>Idempotency-Key</c>: clique duplo ou reenvio devolve o mesmo negócio.</summary>
    [HttpPost]
    [EnableRateLimiting(PoliticaPorIp)]
    public async Task<IActionResult> Cadastrar(
        CadastrarRequisicao requisicao, [FromHeader(Name = "Idempotency-Key")] string? chaveIdempotencia, CancellationToken cancellationToken)
    {
        if (!await _captcha.ValidarAsync(requisicao.TokenCaptcha, Ip, cancellationToken))
            return BadRequest(new ProblemDetails { Title = "Confirme que você não é um robô e tente de novo." });

        var resultado = await _servico.CadastrarAsync(new DadosCadastro(
            requisicao.TokenCadastro, requisicao.PlanoId, requisicao.Periodicidade, requisicao.NomeNegocio, requisicao.TipoNegocio,
            requisicao.Slug, requisicao.Nome, requisicao.Email, requisicao.Telefone, requisicao.Senha, requisicao.AceiteTermos),
            chaveIdempotencia ?? string.Empty, cancellationToken);

        if (resultado.Sucesso)
            return StatusCode(StatusCodes.Status201Created, new { negocioId = resultado.NegocioId, slug = resultado.Slug });

        var problema = new ProblemDetails { Title = resultado.Mensagem, Extensions = { ["codigo"] = resultado.Erro.ToString() } };

        return resultado.Erro switch
        {
            ErroCadastro.TokenInvalido => Unauthorized(problema),
            ErroCadastro.SlugEmUso or ErroCadastro.EmailJaCadastrado or ErroCadastro.TesteJaUtilizado
                or ErroCadastro.ChaveIdempotenciaDeOutroCadastro => Conflict(problema),
            _ => BadRequest(problema),
        };
    }

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();
}
