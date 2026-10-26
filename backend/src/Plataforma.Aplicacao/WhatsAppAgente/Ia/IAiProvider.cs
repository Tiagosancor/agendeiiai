using System.Text.Json;

namespace Plataforma.Aplicacao.WhatsAppAgente.Ia;

public enum PapelIa
{
    Usuario = 1,
    Modelo = 2,
    Ferramenta = 3,
}

/// <summary>
/// Uma mensagem da conversa com o modelo. <c>Usuario</c>/<c>Modelo</c> carregam <see cref="Texto"/>; um turno do modelo pode
/// trazer <see cref="Chamadas"/> (pedidos de ferramenta); <c>Ferramenta</c> carrega o <see cref="Resultado"/> de uma chamada.
/// </summary>
public sealed record AiMensagem(
    PapelIa Papel, string? Texto = null, IReadOnlyList<AiChamadaFerramenta>? Chamadas = null, AiResultadoFerramenta? Resultado = null);

public sealed record AiFerramenta(string Nome, string Descricao, JsonElement EsquemaParametros);

/// <summary>
/// Pedido de ferramenta feito pelo modelo. <see cref="AssinaturaPensamento"/> é o <c>thoughtSignature</c> opaco que os modelos
/// Gemini 3 devolvem na mesma <c>part</c> do <c>functionCall</c> e exigem de volta, idêntico, no turno do modelo. Nunca vai
/// para log, banco nem resposta ao cliente; nulo em modelos que não a enviam.
/// </summary>
public sealed record AiChamadaFerramenta(string Nome, JsonElement Argumentos, string? AssinaturaPensamento = null);

public sealed record AiResultadoFerramenta(string Nome, JsonElement Conteudo);

/// <summary>
/// Pedido ao modelo. <see cref="Modelo"/> nulo = o modelo principal da configuração; preenchido = esse modelo (o de reserva).
/// </summary>
public sealed record AiRequest(
    string PromptSistema, IReadOnlyList<AiMensagem> Mensagens, IReadOnlyList<AiFerramenta> Ferramentas, int MaxTokensSaida,
    string? Modelo = null);

public sealed record AiResponse(
    string? Texto, IReadOnlyList<AiChamadaFerramenta> Chamadas, int TokensEntrada, int TokensSaida, string Modelo, string MotivoFim);

/// <summary>Provedor de IA generativa (hoje o Gemini). Falha → <see cref="FalhaProvedorIaException"/>.</summary>
public interface IAiProvider
{
    Task<AiResponse> GenerateAsync(AiRequest requisicao, CancellationToken cancellationToken = default);
}

/// <summary>
/// Falha do provedor de IA. Carrega só o tipo e o status HTTP — nunca o corpo da resposta (pode ecoar o prompt, que contém
/// texto do cliente).
/// </summary>
public sealed class FalhaProvedorIaException : Exception
{
    public string Tipo { get; }

    public int? StatusHttp { get; }

    /// <summary>
    /// Falha em que outro modelo pode dar certo: timeout, erro de rede e HTTP 404 (modelo aposentado), 408, 429 e 500/502/503/504
    /// (capacidade/instabilidade do lado do provedor). 400/401/403 e resposta inválida NÃO entram: o erro é nosso (pedido ou chave)
    /// e outro modelo só gastaria uma chamada.
    /// </summary>
    public bool FalhaDeCapacidade =>
        Tipo is "timeout" or "rede" || (Tipo == "http" && StatusHttp is 404 or 408 or 429 or 500 or 502 or 503 or 504);

    public FalhaProvedorIaException(string tipo, int? statusHttp = null, Exception? interna = null)
        : base($"Falha do provedor de IA ({tipo}{(statusHttp is null ? string.Empty : $", HTTP {statusHttp}")}).", interna)
    {
        Tipo = tipo;
        StatusHttp = statusHttp;
    }
}
