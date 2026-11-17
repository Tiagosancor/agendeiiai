using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plataforma.Api.Assinaturas;
using Plataforma.Aplicacao.Autenticacao;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Login e renovação do painel (seção 4). O refresh token vai num cookie httpOnly,
/// host-only (sem <c>Domain</c> — seção 8.3.4) e restrito ao caminho <c>/painel/auth</c>.
/// </summary>
[ApiController]
[Route("painel/auth")]
[AllowAnonymous]
[PermitirComAssinaturaSuspensa]
public sealed class AutenticacaoController : ControllerBase
{
    private const string NomeCookieRefresh = "refresh_token";

    public const string PoliticaRedefinicaoSenhaPorIp = "RedefinicaoSenhaPorIp";

    /// <summary>Limite secundário por IP no login (o bloqueio por identificador é a proteção principal).</summary>
    public const string PoliticaLoginPorIp = "LoginPainelPorIp";

    private readonly IServicoAutenticacao _servicoAutenticacao;
    private readonly IServicoRedefinicaoSenha _servicoRedefinicaoSenha;
    private readonly IWebHostEnvironment _ambiente;

    public AutenticacaoController(
        IServicoAutenticacao servicoAutenticacao, IServicoRedefinicaoSenha servicoRedefinicaoSenha, IWebHostEnvironment ambiente)
    {
        _servicoAutenticacao = servicoAutenticacao;
        _servicoRedefinicaoSenha = servicoRedefinicaoSenha;
        _ambiente = ambiente;
    }

    /// <summary>
    /// Entra com e-mail, telefone ou nome de usuário. <c>identificador</c> (novo) ou <c>email</c> (versão antiga do front, ainda
    /// aceita): vale o primeiro preenchido. Falha de qualquer tipo = 401 sem corpo; identificador com falhas demais = 429
    /// <c>muitas_tentativas</c> (sem conferir a senha).
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting(PoliticaLoginPorIp)]
    public async Task<ActionResult<RespostaLogin>> Login(RequisicaoLogin requisicao, CancellationToken cancellationToken)
    {
        var identificador = !string.IsNullOrWhiteSpace(requisicao.Identificador) ? requisicao.Identificador : requisicao.Email;
        if (string.IsNullOrWhiteSpace(identificador))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["identificador"] = ["Informe o e-mail, telefone ou nome de usuário."],
            }));

        var resultado = await _servicoAutenticacao.LoginAsync(identificador, requisicao.Senha, cancellationToken);

        if (resultado.Bloqueado)
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Muitas tentativas. Aguarde alguns minutos e tente de novo.",
                Extensions = { ["codigo"] = "muitas_tentativas" },
            });

        if (!resultado.Sucesso)
            return Unauthorized();

        DefinirCookieRefresh(resultado.RefreshToken!, resultado.RefreshTokenExpiraEm!.Value);
        return Ok(new RespostaLogin(resultado.AccessToken!, resultado.AccessTokenExpiraEm!.Value));
    }

    [HttpPost("renovar")]
    public async Task<ActionResult<RespostaLogin>> Renovar(CancellationToken cancellationToken)
    {
        var refreshTokenBruto = Request.Cookies[NomeCookieRefresh];

        if (string.IsNullOrEmpty(refreshTokenBruto))
            return Unauthorized();

        var resultado = await _servicoAutenticacao.RenovarAsync(refreshTokenBruto, cancellationToken);

        if (!resultado.Sucesso)
        {
            Response.Cookies.Delete(NomeCookieRefresh);
            return Unauthorized();
        }

        DefinirCookieRefresh(resultado.RefreshToken!, resultado.RefreshTokenExpiraEm!.Value);
        return Ok(new RespostaLogin(resultado.AccessToken!, resultado.AccessTokenExpiraEm!.Value));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshTokenBruto = Request.Cookies[NomeCookieRefresh];

        if (!string.IsNullOrEmpty(refreshTokenBruto))
            await _servicoAutenticacao.LogoutAsync(refreshTokenBruto, cancellationToken);

        Response.Cookies.Delete(NomeCookieRefresh);
        return NoContent();
    }

    /// <summary>
    /// "Esqueci minha senha": 202 sempre, exista a conta ou não (anti-enumeração). O link vai
    /// por e-mail e aponta para <c>app.{dominio}/painel/redefinir-senha</c>.
    /// </summary>
    [HttpPost("esqueci-senha")]
    [EnableRateLimiting(PoliticaRedefinicaoSenhaPorIp)]
    public async Task<IActionResult> EsqueciSenha(RequisicaoEsqueciSenha requisicao, CancellationToken cancellationToken)
    {
        await _servicoRedefinicaoSenha.SolicitarAsync(requisicao.Email, cancellationToken);
        return Accepted();
    }

    [HttpPost("redefinir-senha")]
    [EnableRateLimiting(PoliticaRedefinicaoSenhaPorIp)]
    public async Task<IActionResult> RedefinirSenha(RequisicaoRedefinirSenha requisicao, CancellationToken cancellationToken)
    {
        var resultado = await _servicoRedefinicaoSenha.RedefinirAsync(requisicao.Token, requisicao.NovaSenha, cancellationToken);

        return resultado switch
        {
            ResultadoRedefinicaoSenha.Sucesso => NoContent(),
            ResultadoRedefinicaoSenha.SenhaFraca => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "A senha precisa ter pelo menos 8 caracteres, com letras e números."),
            _ => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Este link é inválido ou já expirou. Peça um novo em \"Esqueci minha senha\"."),
        };
    }

    private void DefinirCookieRefresh(string tokenBruto, DateTimeOffset expiraEm)
    {
        Response.Cookies.Append(NomeCookieRefresh, tokenBruto, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_ambiente.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/painel/auth",
            Expires = expiraEm,
            // Domain de propósito ausente: cookie host-only em app.{dominio} (seção 8.3.4).
        });
    }
}

/// <summary>
/// <c>Identificador</c> (e-mail, telefone ou nome de usuário) é o campo novo; <c>Email</c> continua aceito para a versão do front
/// que ainda manda só o e-mail. Ao menos um dos dois é obrigatório (validado na ação).
/// </summary>
public sealed record RequisicaoLogin([StringLength(1000)] string? Email, [Required, StringLength(1000)] string Senha, [StringLength(1000)] string? Identificador = null);

public sealed record RequisicaoEsqueciSenha([Required, StringLength(1000)] string Email);

public sealed record RequisicaoRedefinirSenha([Required] string Token, [Required] string NovaSenha);

public sealed record RespostaLogin(string AccessToken, DateTimeOffset ExpiraEm);
