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

    public ContextoNegocioClaimsTransformation(IContextoNegocio contextoNegocio)
    {
        _contextoNegocio = contextoNegocio;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var negocioIdBruto = principal.FindFirst(ClaimsPlataforma.NegocioId)?.Value;

        if (!_contextoNegocio.TemNegocio && Guid.TryParse(negocioIdBruto, out var negocioId))
            _contextoNegocio.Definir(negocioId);

        return Task.FromResult(principal);
    }
}
