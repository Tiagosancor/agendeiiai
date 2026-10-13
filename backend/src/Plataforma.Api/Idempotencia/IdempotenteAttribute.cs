using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Idempotencia;
using Plataforma.Infraestrutura.Idempotencia;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Api.Idempotencia;

/// <summary>
/// Liga o <c>Idempotency-Key</c> opcional num endpoint do painel. Valida o cabeçalho, calcula o hash canônico da intenção (DTO +
/// valores de rota) e entrega ambos ao <see cref="ContextoIdempotencia"/>; quem reivindica a chave é o serviço de domínio, dentro
/// da própria transação. Depois da ação: devolve 422/409 do mecanismo como <c>ProblemDetails</c> com <c>codigo</c> estável e
/// marca a repetição com <c>Idempotent-Replayed: true</c>. Sem o cabeçalho, nada muda (a menos que <c>Obrigatoria</c> esteja ligada).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed partial class IdempotenteAttribute(string escopo, int statusSucesso = 200) : ActionFilterAttribute
{
    public const string Cabecalho = "Idempotency-Key";

    public const string CabecalhoRepeticao = "Idempotent-Replayed";

    [GeneratedRegex("^[A-Za-z0-9_-]{16,100}$")]
    private static partial Regex FormatoDaChave();

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var servicos = context.HttpContext.RequestServices;
        var opcoes = servicos.GetRequiredService<IOptions<OpcoesIdempotencia>>().Value;
        if (!opcoes.Habilitada)
        {
            await next();
            return;
        }

        var valores = context.HttpContext.Request.Headers[Cabecalho];
        if (valores.Count == 0)
        {
            if (opcoes.Obrigatoria)
            {
                context.Result = Problema(400, IdempotenciaException.CodigoChaveObrigatoria, "Envie o cabeçalho Idempotency-Key nesta operação.");
                return;
            }

            await next();
            return;
        }

        var chave = valores.Count == 1 ? valores[0] : null;
        if (chave is null || !FormatoDaChave().IsMatch(chave))
        {
            context.Result = Problema(400, IdempotenciaException.CodigoChaveInvalida,
                "Idempotency-Key inválida: use de 16 a 100 caracteres entre letras, números, hífen e sublinhado (ex.: um UUID).");
            return;
        }

        servicos.GetRequiredService<ContextoIdempotencia>().Definir(chave, escopo, CalcularHash(context), statusSucesso);

        var executado = await next();

        if (executado.Exception is IdempotenciaException erro)
        {
            if (erro.RetryAfterSegundos is { } segundos)
                context.HttpContext.Response.Headers.RetryAfter = segundos.ToString();
            executado.Result = Problema(erro.StatusHttp, erro.Codigo, erro.Message);
            executado.ExceptionHandled = true;
        }
        else if (servicos.GetRequiredService<ContextoIdempotencia>().Repeticao)
        {
            context.HttpContext.Response.Headers[CabecalhoRepeticao] = "true";
        }
    }

    /// <summary>A intenção: argumentos vindos do corpo, da rota ou da query (nunca token de cancelamento nem serviços injetados).</summary>
    private static string CalcularHash(ActionExecutingContext context)
    {
        var corpo = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        var rota = new Dictionary<string, object?>();

        foreach (var parametro in ((ControllerActionDescriptor)context.ActionDescriptor).Parameters)
        {
            var origem = parametro.BindingInfo?.BindingSource;
            if (!context.ActionArguments.TryGetValue(parametro.Name, out var valor))
                continue;

            if (origem == BindingSource.Body)
                corpo[parametro.Name] = valor;
            else if (origem == BindingSource.Path || origem == BindingSource.Query)
                rota[parametro.Name] = valor;
        }

        return HashDeRequisicao.Calcular(corpo, rota);
    }

    private static ObjectResult Problema(int status, string codigo, string mensagem)
    {
        var problema = new ProblemDetails { Status = status, Title = mensagem };
        problema.Extensions["codigo"] = codigo;
        return new ObjectResult(problema) { StatusCode = status };
    }
}
