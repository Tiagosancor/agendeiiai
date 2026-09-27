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
///   - <c>{dominio}</c> ou <c>{reservado}.{dominio}</c>      → sem tenant por subdomínio (raiz, painel em
///     (<c>app</c>, <c>api</c>, <c>www</c>... — <c>Slug.Reservados</c>)      <c>app</c>, a própria API em <c>api</c>).
///   - <c>{slug}.{dominio}</c>, slug válido e negócio ativo   → tenant resolvido, segue o pipeline.
///   - <c>{slug}.{dominio}</c>, slug inválido/reservado/não   → 404 em rotas <c>/publico/**</c>;
///     encontrado                                               nas demais rotas, segue sem tenant.
///   - host fora do padrão (ex.: localhost puro em testes)    → segue sem tenant, a não ser que
///     o cabeçalho <see cref="CabecalhoSlugInterno"/> esteja presente (ver abaixo).
///
/// <see cref="CabecalhoSlugInterno"/>: usado só pelo proxy same-origin do Next.js
/// (`app/api/publico/[...caminho]/route.ts`) — o navegador chama o próprio host da página
/// (`{slug}.{dominio}:3000`, sem CORS), mas o `fetch()` do Node no servidor do Next.js NÃO
/// consegue repassar um `Host` customizado pro `fetch` de saída até a API (o Node ignora
/// silenciosamente um `Host` manual, sobrescrevendo com o da URL de destino — testado
/// manualmente, não é bug deste projeto). O proxy já calculou o slug a partir do Host real
/// recebido do navegador (a mesma fonte confiável de sempre — seção 8.3.2, nunca um valor
/// arbitrário do cliente final), só não consegue repassar isso via `Host`; manda por este
/// cabeçalho em vez disso. Só entra em jogo quando o Host da requisição em si não resolveu
/// nenhum slug (ex.: chamada interna `api:8080`), então nunca é usado pra sobrepor uma
/// resolução por Host que já funcionou.
/// </summary>
public sealed class ResolucaoNegocioMiddleware
{
    private const string PrefixoRotasPublicas = "/publico";
    private const string CabecalhoSlugInterno = "X-Slug-Negocio";

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

        string? candidatoSlug = null;

        // Subdomínio reservado (app, api, www...) nunca é negócio: é o host da própria
        // plataforma. Sem isso, a API publicada em api.{dominio} tratava "api" como slug e
        // respondia 404 em TODA rota /publico — inclusive as que o frontend chama com o
        // cabeçalho abaixo (bug real de produção; em dev a API fica em localhost e não aparecia).
        if (host != dominioBase && host.EndsWith(sufixo, StringComparison.Ordinal))
        {
            var subdominio = host[..^sufixo.Length];
            if (!Slug.Reservados.Contains(subdominio))
                candidatoSlug = subdominio;
        }

        if (candidatoSlug is null
            && contexto.Request.Headers.TryGetValue(CabecalhoSlugInterno, out var valorCabecalho)
            && !string.IsNullOrWhiteSpace(valorCabecalho))
        {
            candidatoSlug = valorCabecalho.ToString();
        }

        if (candidatoSlug is null)
        {
            await _proximo(contexto);
            return;
        }

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
