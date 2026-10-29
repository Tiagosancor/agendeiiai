using System.Globalization;
using System.Text;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

public enum RespostaDoCliente
{
    /// <summary>Qualquer coisa que não seja, por inteiro, um "sim" ou um "não" da lista fechada: segue para a IA.</summary>
    Outra = 0,
    Afirmativa = 1,
    Negativa = 2,
}

/// <summary>
/// Avalia, PELO SERVIDOR e sem IA, se o que o cliente respondeu ao resumo do agendamento é um "sim" ou um "não". Lista fechada
/// de palavras, depois de normalizar (minúsculas, sem acento, sem pontuação, espaços colapsados); texto com mais de 30
/// caracteres nunca conta. Num lote de várias mensagens TODAS precisam ser do mesmo lado — "sim" seguido de "na verdade 15h" não
/// confirma nada. Puro.
/// </summary>
public static class ClassificadorConfirmacaoAgente
{
    public const int TamanhoMaximoMensagem = 30;

    private static readonly HashSet<string> Afirmativas = new(StringComparer.Ordinal)
    {
        "sim", "s", "ss", "sim sim", "confirmo", "confirma", "confirmar", "confirmado", "pode confirmar", "sim pode",
        "sim confirmo", "pode", "ok", "isso", "certo", "fechado", "beleza", "👍", "✅",
    };

    private static readonly HashSet<string> Negativas = new(StringComparer.Ordinal)
    {
        "nao", "n", "nao quero", "cancela", "cancelar", "desisto", "desisti",
    };

    // Cancelar (gestão): lista MAIS ESTRITA. Cancelar é irreversível, então "ok", "beleza", "pode", "isso" e "👍" não bastam, e "cancela"
    // sozinho é ambíguo (pode ser o pedido inicial repetido) e vai para a IA — diferente do agendamento, onde "cancela" é negativo.
    private static readonly HashSet<string> AfirmativasCancelamento = new(StringComparer.Ordinal)
    {
        "sim", "s", "confirmo", "sim confirmo", "pode cancelar", "sim pode cancelar", "sim cancela",
    };

    private static readonly HashSet<string> NegativasCancelamento = new(StringComparer.Ordinal)
    {
        "nao", "n", "nao quero", "nao cancela", "nao cancelar", "desisto", "desisti", "mantem", "manter", "deixa", "deixa assim",
    };

    public static RespostaDoCliente Classificar(string? mensagem) => ClassificarCom(mensagem, Afirmativas, Negativas);

    /// <summary>"Sim" ou "não" a um resumo de CANCELAMENTO: lista estrita (ver <see cref="AfirmativasCancelamento"/>).</summary>
    public static RespostaDoCliente ClassificarCancelamento(string? mensagem) =>
        ClassificarCom(mensagem, AfirmativasCancelamento, NegativasCancelamento);

    private static RespostaDoCliente ClassificarCom(string? mensagem, HashSet<string> afirmativas, HashSet<string> negativas)
    {
        if (string.IsNullOrWhiteSpace(mensagem) || mensagem.Trim().Length > TamanhoMaximoMensagem)
            return RespostaDoCliente.Outra;

        var normalizada = Normalizar(mensagem);
        if (afirmativas.Contains(normalizada))
            return RespostaDoCliente.Afirmativa;

        return negativas.Contains(normalizada) ? RespostaDoCliente.Negativa : RespostaDoCliente.Outra;
    }

    /// <summary>Lote vazio é <c>Outra</c>. Mistura de "sim" e "não", ou qualquer mensagem fora da lista, também.</summary>
    public static RespostaDoCliente ClassificarLote(IReadOnlyCollection<string?> mensagens) => ClassificarLoteCom(mensagens, Classificar);

    /// <summary>Igual a <see cref="ClassificarLote"/>, com a lista estrita do cancelamento.</summary>
    public static RespostaDoCliente ClassificarLoteCancelamento(IReadOnlyCollection<string?> mensagens) =>
        ClassificarLoteCom(mensagens, ClassificarCancelamento);

    private static RespostaDoCliente ClassificarLoteCom(IReadOnlyCollection<string?> mensagens, Func<string?, RespostaDoCliente> classificar)
    {
        if (mensagens.Count == 0)
            return RespostaDoCliente.Outra;

        var primeira = classificar(mensagens.First());
        if (primeira == RespostaDoCliente.Outra)
            return RespostaDoCliente.Outra;

        return mensagens.All(m => classificar(m) == primeira) ? primeira : RespostaDoCliente.Outra;
    }

    /// <summary>Minúsculas, sem acento, só letras/dígitos/emoji (pontuação vira espaço), espaços colapsados.</summary>
    public static string Normalizar(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var runa in decomposto.EnumerateRunes())
        {
            switch (Rune.GetUnicodeCategory(runa))
            {
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.ModifierSymbol: // tom de pele do emoji
                case UnicodeCategory.Format: // ZWJ, seletor de variação
                    break;
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.DecimalDigitNumber:
                    sb.Append(Rune.ToLowerInvariant(runa).ToString());
                    break;
                case UnicodeCategory.OtherSymbol:
                    sb.Append(runa.ToString());
                    break;
                default:
                    sb.Append(' ');
                    break;
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
