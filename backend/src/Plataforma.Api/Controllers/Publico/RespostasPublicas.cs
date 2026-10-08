using Microsoft.AspNetCore.Mvc;

namespace Plataforma.Api.Controllers.Publico;

/// <summary>Respostas padrão das rotas públicas que o middleware de resolução também produz.</summary>
internal static class RespostasPublicas
{
    /// <summary>
    /// Mesmo 404 do middleware para slug desconhecido: sem negócio resolvido e negócio inexistente não se distinguem.
    /// Rede de segurança caso uma rota chegue ao controller sem o contexto (nunca dereferenciar NegocioId às cegas).
    /// </summary>
    public static ObjectResult NegocioNaoEncontrado(HttpContext contexto)
    {
        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Negócio não encontrado.",
            Detail = "Não existe um negócio ativo para este endereço.",
            Instance = contexto.Request.Path,
        };

        return new ObjectResult(problema) { StatusCode = StatusCodes.Status404NotFound };
    }
}
