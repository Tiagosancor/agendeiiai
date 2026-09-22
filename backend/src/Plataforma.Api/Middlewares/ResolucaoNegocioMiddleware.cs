using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Api.Middlewares;

/// <summary>
/// Resolve o negócio (tenant) pelo host da requisição, para as rotas públicas
/// (seção 8.3.2: "nos endpoints públicos, o negócio é resolvido pelo slug (host)").
/// Rotas do painel (<c>/painel/**</c> ou equivalente autenticado) não passam por aqui —
/// o tenant delas vem do JWT, nunca do host.
///
/// Hosts reconhecidos, dado o domínio base configurado em <c>Marca__Dominio</c>:
///   - <c>{dominio}</c> ou <c>app.{dominio}</c>              → sem tenant por subdomínio (painel/raiz).
///   - <c>{slug}.{dominio}</c>, slug válido e negócio ativo   → tenant resolvido, segue o pipeline.
///   - <c>{slug}.{dominio}</c>, slug inválido/reservado/não   → 404 em rotas <c>/publico/**</c>;
///     encontrado                                               nas demais rotas, segue sem tenant.
///   - host fora do padrão (ex.: localhost puro em testes)    → segue sem tenant.
/// </summary>
public sealed class ResolucaoNegocioMiddleware
{
    private const string PrefixoRotasPublicas = "/publico";

    private readonly RequestDelegate _proximo;
    private readonly IOptions<OpcoesMarca> _opcoesMarca;
    private readonly ILogger<ResolucaoNegocioMiddleware> _logger;

    public ResolucaoNegocioMiddleware(
        RequestDelegate proximo, IOptions<OpcoesMarca> opcoesMarca, ILogger<ResolucaoNegocioMiddleware> logger)
    {
        _proximo = proximo;
        _opcoesMarca = opcoesMarca;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext contexto, IContextoNegocio contextoNegocio, IConsultaNegocioPublico consultaNegocio)
    {
        var host = contexto.Request.Host.Host.ToLowerInvariant();
        var dominioBase = _opcoesMarca.Value.Dominio.ToLowerInvariant();
        var sufixo = "." + dominioBase;

        var ehRotaPublica = contexto.Request.Path.StartsWithSegments(PrefixoRotasPublicas);

        if (host == dominioBase || host == "app." + dominioBase || !host.EndsWith(sufixo, StringComparison.Ordinal))
        {
            await _proximo(contexto);
            return;
        }

        var candidatoSlug = host[..^sufixo.Length];

        if (!Slug.TentarCriar(candidatoSlug, out var slug))
        {
            if (ehRotaPublica)
            {
                await EscreverNaoEncontradoAsync(contexto);
                return;
            }

            await _proximo(contexto);
            return;
        }

        var negocio = await consultaNegocio.ObterPorSlugAsync(slug!, contexto.RequestAborted);

        if (negocio is null)
        {
            _logger.LogInformation("Nenhum negócio ativo encontrado para o slug {Slug}", slug);

            if (ehRotaPublica)
            {
                await EscreverNaoEncontradoAsync(contexto);
                return;
            }

            await _proximo(contexto);
            return;
        }

        contextoNegocio.Definir(negocio.Id);
        contexto.Items[nameof(NegocioResumo)] = negocio;

        await _proximo(contexto);
    }

    private static async Task EscreverNaoEncontradoAsync(HttpContext contexto)
    {
        contexto.Response.StatusCode = StatusCodes.Status404NotFound;
        contexto.Response.ContentType = "application/problem+json";

        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Negócio não encontrado.",
            Detail = "Não existe um negócio ativo para este endereço.",
            Instance = contexto.Request.Path,
        };

        await contexto.Response.WriteAsJsonAsync(problema);
    }
}
