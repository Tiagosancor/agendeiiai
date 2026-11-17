using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Traduz os erros de acesso (identificadores, senha, código) para <c>ProblemDetails</c> com <c>codigo</c>. Nunca leva o valor
/// do identificador: o 409 diz só o <c>campo</c> (e, no nome de usuário, uma <c>sugestao</c>).
/// </summary>
internal static class RespostasAcesso
{
    public static ObjectResult? Traduzir(Exception excecao) => excecao switch
    {
        ErroDeAcessoException erro when erro.Codigo == "usuario_nao_encontrado" => Problema(StatusCodes.Status404NotFound, erro.Message, erro.Codigo),
        ErroDeAcessoException erro => Problema(StatusCodes.Status400BadRequest, erro.Message, erro.Codigo),
        IdentificadorObrigatorioException erro => Problema(StatusCodes.Status400BadRequest, erro.Message, "identificador_obrigatorio"),
        IdentificadorIndisponivelException erro => Indisponivel(erro),
        MuitasTentativasException erro => Problema(StatusCodes.Status429TooManyRequests, erro.Message, "muitas_tentativas"),
        _ => null,
    };

    private static ObjectResult Indisponivel(IdentificadorIndisponivelException erro)
    {
        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Identificador indisponível.",
            Detail = erro.Message,
            Extensions = { ["codigo"] = "identificador_indisponivel", ["campo"] = erro.Campo },
        };
        if (erro.Sugestao is not null)
            problema.Extensions["sugestao"] = erro.Sugestao;

        return new ObjectResult(problema) { StatusCode = StatusCodes.Status409Conflict };
    }

    private static ObjectResult Problema(int status, string titulo, string codigo) => new(new ProblemDetails
    {
        Status = status,
        Title = titulo,
        Extensions = { ["codigo"] = codigo },
    })
    { StatusCode = status };
}

/// <summary>Filtro de exceção das controllers de acesso: devolve 400/404/409/429 com <c>codigo</c> em vez de 500.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class TraduzirErrosDeAcessoAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext contexto)
    {
        var resposta = RespostasAcesso.Traduzir(contexto.Exception);
        if (resposta is null)
            return;

        contexto.Result = resposta;
        contexto.ExceptionHandled = true;
    }
}
