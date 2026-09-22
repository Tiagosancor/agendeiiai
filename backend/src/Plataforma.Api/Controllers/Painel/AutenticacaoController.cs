using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Autenticacao;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Login e renovação do painel (seção 4). O refresh token vai num cookie httpOnly,
/// host-only (sem <c>Domain</c> — seção 8.3.4) e restrito ao caminho <c>/painel/auth</c>.
/// </summary>
[ApiController]
[Route("painel/auth")]
[AllowAnonymous]
public sealed class AutenticacaoController : ControllerBase
{
    private const string NomeCookieRefresh = "refresh_token";

    private readonly IServicoAutenticacao _servicoAutenticacao;
    private readonly IWebHostEnvironment _ambiente;

    public AutenticacaoController(IServicoAutenticacao servicoAutenticacao, IWebHostEnvironment ambiente)
    {
        _servicoAutenticacao = servicoAutenticacao;
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

public sealed record RespostaLogin(string AccessToken, DateTimeOffset ExpiraEm);
