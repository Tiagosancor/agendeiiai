using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Plataforma.Aplicacao.Abstracoes;

namespace Plataforma.Api.Autenticacao;

/// <summary>
/// No painel, o negócio vem sempre do JWT, nunca de parâmetro/corpo/cabeçalho (seção 8.3.2).
/// Roda depois que a autenticação valida o token e antes de qualquer endpoint, preenchendo
/// o <see cref="IContextoNegocio"/> — o mesmo mecanismo que o <c>ResolucaoNegocioMiddleware</c>
/// usa para as rotas públicas (resolvidas pelo host), só que aqui a fonte é a claim do token.
/// </summary>
public sealed class ContextoNegocioClaimsTransformation : IClaimsTransformation
{
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IUsuarioAtual _usuarioAtual;

    public ContextoNegocioClaimsTransformation(IContextoNegocio contextoNegocio, IUsuarioAtual usuarioAtual)
    {
        _contextoNegocio = contextoNegocio;
        _usuarioAtual = usuarioAtual;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var negocioIdBruto = principal.FindFirst(ClaimsPlataforma.NegocioId)?.Value;

        if (!_contextoNegocio.TemNegocio && Guid.TryParse(negocioIdBruto, out var negocioId))
            _contextoNegocio.Definir(negocioId);

        // Autor das ações (log de auditoria, "ninguém exclui o próprio usuário" — seção 7).
        if (_usuarioAtual.UsuarioId is null && Guid.TryParse(ObterUsuarioId(principal), out var usuarioId))
            _usuarioAtual.Definir(usuarioId, principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst("email")?.Value);

        return Task.FromResult(principal);
    }

    /// <summary>"sub" do token — o manipulador de JWT pode tê-lo mapeado para NameIdentifier.</summary>
    public static string? ObterUsuarioId(ClaimsPrincipal principal) =>
        principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
