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

public sealed record AiChamadaFerramenta(string Nome, JsonElement Argumentos);

public sealed record AiResultadoFerramenta(string Nome, JsonElement Conteudo);

public sealed record AiRequest(
    string PromptSistema, IReadOnlyList<AiMensagem> Mensagens, IReadOnlyList<AiFerramenta> Ferramentas, int MaxTokensSaida);

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

    public FalhaProvedorIaException(string tipo, int? statusHttp = null, Exception? interna = null)
        : base($"Falha do provedor de IA ({tipo}{(statusHttp is null ? string.Empty : $", HTTP {statusHttp}")}).", interna)
    {
        Tipo = tipo;
        StatusHttp = statusHttp;
    }
}
