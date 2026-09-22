using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Api.Middlewares;

/// <summary>
/// Modo manutenção (seção 8.5.8) — 503 amigável em toda a API (o que já cobre "criação de
/// agendamentos bloqueada", já que bloqueia tudo) enquanto uma migração de hospedagem está
/// em andamento. Ligado só por variável de ambiente (<c>Manutencao__Ativo</c>) — variável
/// de ambiente não recarrega sozinha em runtime, então ativar/desativar exige reiniciar o
/// container (`docker compose up -d` de novo com a variável trocada), o que é esperado
/// mesmo: a virada de uma migração já envolve reiniciar/reapontar o serviço de qualquer
/// jeito. Usa <see cref="IOptionsMonitor{TOptions}"/> em vez de <c>IOptions</c> só por
/// consistência de padrão caso um dia a config venha de uma fonte que recarrega de
/// verdade (ex.: Azure App Configuration) — hoje, com env var, o efeito prático é o mesmo
/// de <c>IOptions</c>. <c>/health</c> continua respondendo normalmente, pra orquestradores
/// não derrubarem o container achando que crashou.
/// </summary>
public sealed class ModoManutencaoMiddleware
{
    private readonly RequestDelegate _proximo;

    public ModoManutencaoMiddleware(RequestDelegate proximo)
    {
        _proximo = proximo;
    }

    public async Task InvokeAsync(HttpContext contexto, IOptionsMonitor<OpcoesManutencao> opcoesManutencao)
    {
        var emManutencao = opcoesManutencao.CurrentValue.Ativo;
        var ehHealthCheck = contexto.Request.Path.StartsWithSegments("/health");

        if (!emManutencao || ehHealthCheck)
        {
            await _proximo(contexto);
            return;
        }

        contexto.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        contexto.Response.ContentType = "application/problem+json";
        contexto.Response.Headers.RetryAfter = "300";

        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Em manutenção.",
            Detail = "Estamos com uma manutenção rápida em andamento. Tente novamente em alguns minutos.",
            Instance = contexto.Request.Path,
        };

        await contexto.Response.WriteAsJsonAsync(problema);
    }
}
