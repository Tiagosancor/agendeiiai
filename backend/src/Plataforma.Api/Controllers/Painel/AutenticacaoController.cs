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

    [HttpPost("login")]
    public async Task<ActionResult<RespostaLogin>> Login(RequisicaoLogin requisicao, CancellationToken cancellationToken)
    {
        var resultado = await _servicoAutenticacao.LoginAsync(requisicao.Email, requisicao.Senha, cancellationToken);

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

public sealed record RequisicaoLogin([Required] string Email, [Required] string Senha);

public sealed record RequisicaoEsqueciSenha([Required] string Email);

public sealed record RequisicaoRedefinirSenha([Required] string Token, [Required] string NovaSenha);

public sealed record RespostaLogin(string AccessToken, DateTimeOffset ExpiraEm);
