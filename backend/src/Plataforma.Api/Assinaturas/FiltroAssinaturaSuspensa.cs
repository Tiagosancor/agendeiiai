using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Assinaturas;

namespace Plataforma.Api.Assinaturas;

/// <summary>Rota do painel que continua acessível com a assinatura suspensa (seção 7: tela de assinatura e exportação LGPD).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PermitirComAssinaturaSuspensaAttribute : Attribute;

/// <summary>Rota pública que cria agendamento novo — recusada com a assinatura suspensa.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class BloquearComAssinaturaSuspensaAttribute : Attribute;

/// <summary>
/// Bloqueio da suspensão aplicado na API, nunca só escondendo botões (seção 8.6.5).
/// Painel: tudo bloqueado por padrão (402), exceto o que for marcado com
/// <see cref="PermitirComAssinaturaSuspensaAttribute"/>. Público: só o que for marcado com
/// <see cref="BloquearComAssinaturaSuspensaAttribute"/>, com mensagem neutra (403) que não
/// revela problema de pagamento. Agendamentos já marcados, cancelar/remarcar e lembretes seguem.
/// </summary>
public sealed class FiltroAssinaturaSuspensa : IAsyncActionFilter
{
    public const string CodigoAssinaturaSuspensa = "assinatura_suspensa";

    private readonly IContextoNegocio _contextoNegocio;
    private readonly IConsultaSituacaoAssinatura _consulta;

    public FiltroAssinaturaSuspensa(IContextoNegocio contextoNegocio, IConsultaSituacaoAssinatura consulta)
    {
        _contextoNegocio = contextoNegocio;
        _consulta = consulta;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext contexto, ActionExecutionDelegate proximo)
    {
        var metadados = contexto.ActionDescriptor.EndpointMetadata;
        var ehPainel = contexto.HttpContext.Request.Path.StartsWithSegments("/painel");
        var bloqueiaPainel = ehPainel && !metadados.OfType<PermitirComAssinaturaSuspensaAttribute>().Any();
        var bloqueiaPublico = metadados.OfType<BloquearComAssinaturaSuspensaAttribute>().Any();

        if ((!bloqueiaPainel && !bloqueiaPublico) || _contextoNegocio.NegocioId is not Guid negocioId)
        {
            await proximo();
            return;
        }

        var situacao = await _consulta.ObterAsync(negocioId, contexto.HttpContext.RequestAborted);
        if (situacao is null || situacao.PermiteOperar)
        {
            await proximo();
            return;
        }

        contexto.Result = bloqueiaPainel
            ? new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status402PaymentRequired,
                Title = "Assinatura suspensa.",
                Detail = "Regularize a assinatura para voltar a usar o painel. Seus dados continuam guardados.",
                Extensions = { ["codigo"] = CodigoAssinaturaSuspensa },
            }) { StatusCode = StatusCodes.Status402PaymentRequired }
            : new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Este negócio não está recebendo agendamentos online no momento.",
                Detail = "Entre em contato pelo telefone do negócio para agendar.",
            }) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
