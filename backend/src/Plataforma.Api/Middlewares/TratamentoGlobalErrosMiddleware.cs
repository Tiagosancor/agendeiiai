using System.Net;
using Microsoft.AspNetCore.Mvc;

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
