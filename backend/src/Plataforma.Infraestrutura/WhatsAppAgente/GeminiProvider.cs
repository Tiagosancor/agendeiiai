using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.WhatsAppAgente.Ia;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// <c>IAiProvider</c> do Google Gemini (<c>generateContent</c>). A chave vai no cabeçalho <c>x-goog-api-key</c>, NUNCA na
/// query (a URL aparece em log). Falha vira <see cref="FalhaProvedorIaException"/> sem o corpo da resposta (pode ecoar o
/// prompt). Uma nova tentativa só em 429/503; timeout não repete (mesma lição do <c>MensageriaWhatsAppEvolution</c>).
/// </summary>
public sealed class GeminiProvider : IAiProvider
{
    public const string CabecalhoChave = "x-goog-api-key";

    private static readonly TimeSpan EsperaAntesDeRepetir = TimeSpan.FromMilliseconds(800);

    private readonly HttpClient _http;
    private readonly IOptions<OpcoesIa> _opcoes;

    public GeminiProvider(HttpClient http, IOptions<OpcoesIa> opcoes)
    {
        _http = http;
        _opcoes = opcoes;
    }

    public async Task<AiResponse> GenerateAsync(AiRequest requisicao, CancellationToken cancellationToken = default)
    {
        var gemini = _opcoes.Value.Gemini;
        var modelo = gemini.Model;
        var url = $"{gemini.UrlBase.TrimEnd('/')}/models/{Uri.EscapeDataString(modelo)}:generateContent";
        var corpo = MontarCorpo(requisicao);

        for (var tentativa = 1; ; tentativa++)
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limite.CancelAfter(TimeSpan.FromSeconds(gemini.TimeoutSegundos));

            HttpResponseMessage resposta;
            try
            {
                using var mensagem = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(corpo, Encoding.UTF8, "application/json"),
                };
                mensagem.Headers.Add(CabecalhoChave, gemini.ApiKey);
                resposta = await _http.SendAsync(mensagem, HttpCompletionOption.ResponseContentRead, limite.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Nosso timeout: não repete (o provedor segura a resposta; repetir dobraria a espera do cliente).
                throw new FalhaProvedorIaException("timeout");
            }
            catch (HttpRequestException excecao)
            {
                throw new FalhaProvedorIaException("rede", (int?)excecao.StatusCode);
            }

            using (resposta)
            {
                var status = (int)resposta.StatusCode;

                if (status is 429 or 503 && tentativa == 1)
                {
                    try
                    {
                        await Task.Delay(EsperaAntesDeRepetir, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw new FalhaProvedorIaException("http", status);
                    }

                    continue;
                }

                if (!resposta.IsSuccessStatusCode)
                    throw new FalhaProvedorIaException("http", status);

                string texto;
                try
                {
                    texto = await resposta.Content.ReadAsStringAsync(limite.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new FalhaProvedorIaException("timeout");
                }

                return Interpretar(texto, modelo);
            }
        }
    }

    // ---------------------------------------------------------------- pedido

    public static string MontarCorpo(AiRequest requisicao)
    {
        using var saida = new MemoryStream();
        using (var w = new Utf8JsonWriter(saida))
        {
            w.WriteStartObject();

            w.WriteStartObject("systemInstruction");
            w.WriteStartArray("parts");
            w.WriteStartObject();
            w.WriteString("text", requisicao.PromptSistema);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartArray("contents");
            EscreverConteudos(w, requisicao.Mensagens);
            w.WriteEndArray();

            if (requisicao.Ferramentas.Count > 0)
            {
                w.WriteStartArray("tools");
                w.WriteStartObject();
                w.WriteStartArray("functionDeclarations");
                foreach (var ferramenta in requisicao.Ferramentas)
                {
                    w.WriteStartObject();
                    w.WriteString("name", ferramenta.Nome);
                    w.WriteString("description", ferramenta.Descricao);
                    if (TemParametros(ferramenta.EsquemaParametros))
                    {
                        w.WritePropertyName("parameters");
                        EscreverEsquema(w, ferramenta.EsquemaParametros);
                    }

                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteEndObject();
                w.WriteEndArray();
            }

            w.WriteStartObject("generationConfig");
            w.WriteNumber("maxOutputTokens", requisicao.MaxTokensSaida);
            w.WriteNumber("temperature", 0.2);
            w.WriteEndObject();

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(saida.ToArray());
    }

    /// <summary>Turnos consecutivos do mesmo papel viram um só (resultados de várias ferramentas = uma mensagem de usuário).</summary>
    private static void EscreverConteudos(Utf8JsonWriter w, IReadOnlyList<AiMensagem> mensagens)
    {
        string? papelAberto = null;
        foreach (var mensagem in mensagens)
        {
            var papel = mensagem.Papel == PapelIa.Modelo ? "model" : "user";
            if (papel != papelAberto)
            {
                if (papelAberto is not null)
                {
                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteStartObject();
                w.WriteString("role", papel);
                w.WriteStartArray("parts");
                papelAberto = papel;
            }

            if (!string.IsNullOrEmpty(mensagem.Texto))
            {
                w.WriteStartObject();
                w.WriteString("text", mensagem.Texto);
                w.WriteEndObject();
            }

            if (mensagem.Chamadas is { Count: > 0 })
            {
                foreach (var chamada in mensagem.Chamadas)
                {
                    w.WriteStartObject();
                    w.WriteStartObject("functionCall");
                    w.WriteString("name", chamada.Nome);
                    w.WritePropertyName("args");
                    chamada.Argumentos.WriteTo(w);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }
            }

            if (mensagem.Resultado is { } resultado)
            {
                w.WriteStartObject();
                w.WriteStartObject("functionResponse");
                w.WriteString("name", resultado.Nome);
                w.WritePropertyName("response");
                // functionResponse.response precisa ser um objeto; listas vão embrulhadas.
                if (resultado.Conteudo.ValueKind == JsonValueKind.Object)
                {
                    resultado.Conteudo.WriteTo(w);
                }
                else
                {
                    w.WriteStartObject();
                    w.WritePropertyName("resultado");
                    resultado.Conteudo.WriteTo(w);
                    w.WriteEndObject();
                }

                w.WriteEndObject();
                w.WriteEndObject();
            }
        }

        if (papelAberto is not null)
        {
            w.WriteEndArray();
            w.WriteEndObject();
        }
    }

    private static bool TemParametros(JsonElement esquema) =>
        esquema.ValueKind == JsonValueKind.Object
        && esquema.TryGetProperty("properties", out var propriedades)
        && propriedades.ValueKind == JsonValueKind.Object
        && propriedades.EnumerateObject().Any();

    /// <summary>
    /// O Gemini aceita um subconjunto do JSON Schema: sem <c>additionalProperties</c>, tipos em maiúsculas. A validação
    /// estrita de argumentos extras é nossa (o servidor recusa o que não está no esquema).
    /// </summary>
    private static void EscreverEsquema(Utf8JsonWriter w, JsonElement esquema)
    {
        switch (esquema.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var propriedade in esquema.EnumerateObject())
                {
                    if (propriedade.Name == "additionalProperties")
                        continue;

                    w.WritePropertyName(propriedade.Name);
                    if (propriedade.Name == "type" && propriedade.Value.ValueKind == JsonValueKind.String)
                        w.WriteStringValue(propriedade.Value.GetString()!.ToUpperInvariant());
                    else
                        EscreverEsquema(w, propriedade.Value);
                }

                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var item in esquema.EnumerateArray())
                    EscreverEsquema(w, item);
                w.WriteEndArray();
                break;
            default:
                esquema.WriteTo(w);
                break;
        }
    }

    // ---------------------------------------------------------------- resposta

    public static AiResponse Interpretar(string corpo, string modeloPadrao)
    {
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            var raiz = documento.RootElement;

            var tokensEntrada = 0;
            var tokensSaida = 0;
            if (raiz.TryGetProperty("usageMetadata", out var uso) && uso.ValueKind == JsonValueKind.Object)
            {
                tokensEntrada = Inteiro(uso, "promptTokenCount");
                // Tokens de "raciocínio" do modelo são cobrados como saída.
                tokensSaida = Inteiro(uso, "candidatesTokenCount") + Inteiro(uso, "thoughtsTokenCount");
            }

            var modelo = raiz.TryGetProperty("modelVersion", out var versao) && versao.ValueKind == JsonValueKind.String
                ? versao.GetString()!
                : modeloPadrao;

            var texto = new StringBuilder();
            var chamadas = new List<AiChamadaFerramenta>();
            var motivo = "SEM_CANDIDATO";

            if (raiz.TryGetProperty("candidates", out var candidatos) && candidatos.ValueKind == JsonValueKind.Array && candidatos.GetArrayLength() > 0)
            {
                var candidato = candidatos[0];
                motivo = candidato.TryGetProperty("finishReason", out var fim) && fim.ValueKind == JsonValueKind.String ? fim.GetString()! : "DESCONHECIDO";

                if (candidato.TryGetProperty("content", out var conteudo) && conteudo.ValueKind == JsonValueKind.Object
                    && conteudo.TryGetProperty("parts", out var partes) && partes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var parte in partes.EnumerateArray())
                    {
                        if (parte.ValueKind != JsonValueKind.Object)
                            continue;

                        if (parte.TryGetProperty("functionCall", out var chamada) && chamada.ValueKind == JsonValueKind.Object
                            && chamada.TryGetProperty("name", out var nome) && nome.ValueKind == JsonValueKind.String)
                        {
                            var argumentos = chamada.TryGetProperty("args", out var args) ? args.Clone() : EmptyObject();
                            chamadas.Add(new AiChamadaFerramenta(nome.GetString()!, argumentos));
                        }
                        else if (parte.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                            && !(parte.TryGetProperty("thought", out var pensamento) && pensamento.ValueKind == JsonValueKind.True))
                        {
                            texto.Append(t.GetString());
                        }
                    }
                }
            }

            return new AiResponse(texto.Length == 0 ? null : texto.ToString(), chamadas, tokensEntrada, tokensSaida, modelo, motivo);
        }
        catch (JsonException excecao)
        {
            throw new FalhaProvedorIaException("resposta_invalida", null, excecao);
        }
    }

    private static int Inteiro(JsonElement objeto, string propriedade) =>
        objeto.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static JsonElement EmptyObject()
    {
        using var d = JsonDocument.Parse("{}");
        return d.RootElement.Clone();
    }
}
