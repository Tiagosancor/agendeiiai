using System.Net;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Dominio.Comissoes;

namespace Plataforma.Api.Middlewares;

/// <summary>
/// Captura qualquer exceção não tratada e devolve um <c>ProblemDetails</c> genérico.
/// Nunca vaza mensagem de exceção, stack trace ou detalhe de banco para o cliente
/// (seção 8.2.4) — o detalhe completo só vai para o log estruturado (Serilog), e sem
/// dado pessoal (seção 8.1.6).
/// </summary>
public sealed class TratamentoGlobalErrosMiddleware
{
    private readonly RequestDelegate _proximo;
    private readonly ILogger<TratamentoGlobalErrosMiddleware> _logger;

    public TratamentoGlobalErrosMiddleware(RequestDelegate proximo, ILogger<TratamentoGlobalErrosMiddleware> logger)
    {
        _proximo = proximo;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await _proximo(contexto);
        }
        catch (QuinzenaFechadaException excecao) when (!contexto.Response.HasStarted)
        {
            // Regra de negócio, não erro (seção 7): 409 em qualquer rota que mexa no atendimento.
            // Na página pública, texto neutro — o cliente final não sabe nada de comissão.
            var publico = contexto.Request.Path.StartsWithSegments("/publico");
            contexto.Response.ContentType = "application/problem+json";
            contexto.Response.StatusCode = StatusCodes.Status409Conflict;
            await contexto.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = publico ? "Não é possível alterar este agendamento pelo link. Fale com o estabelecimento." : excecao.Message,
                Detail = publico ? null : excecao.Message,
                Extensions = { ["codigo"] = publico ? "nao_permitido" : "quinzena_fechada" },
            });
        }
        catch (Exception excecao)
        {
            _logger.LogError(excecao, "Erro não tratado ao processar {Metodo} {Caminho}",
                contexto.Request.Method, contexto.Request.Path);

            if (contexto.Response.HasStarted)
                throw;

            contexto.Response.ContentType = "application/problem+json";
            contexto.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var problema = new ProblemDetails
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Title = "Ocorreu um erro inesperado.",
                Detail = "Tente novamente em instantes. Se o problema persistir, contate o suporte.",
                Instance = contexto.Request.Path,
            };

            await contexto.Response.WriteAsJsonAsync(problema);
        }
    }
}
