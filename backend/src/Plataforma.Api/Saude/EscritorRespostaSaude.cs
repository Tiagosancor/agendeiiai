using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Plataforma.Api.Saude;

/// <summary>Formata a resposta de <c>/health</c> como JSON com o status de cada verificação (ex.: postgres, btree_gist).</summary>
public static class EscritorRespostaSaude
{
    private static readonly JsonSerializerOptions OpcoesJson = new() { WriteIndented = false };

    public static Task EscreverAsync(HttpContext contexto, HealthReport relatorio)
    {
        contexto.Response.ContentType = "application/json";

        var payload = new
        {
            status = relatorio.Status.ToString(),
            verificacoes = relatorio.Entries.Select(entrada => new
            {
                nome = entrada.Key,
                status = entrada.Value.Status.ToString(),
                descricao = entrada.Value.Description,
                duracaoMs = entrada.Value.Duration.TotalMilliseconds,
            }),
        };

        return contexto.Response.WriteAsync(JsonSerializer.Serialize(payload, OpcoesJson));
    }
}
