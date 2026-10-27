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

    public static RespostaDoCliente Classificar(string? mensagem)
    {
        if (string.IsNullOrWhiteSpace(mensagem) || mensagem.Trim().Length > TamanhoMaximoMensagem)
            return RespostaDoCliente.Outra;

        var normalizada = Normalizar(mensagem);
        if (Afirmativas.Contains(normalizada))
            return RespostaDoCliente.Afirmativa;

        return Negativas.Contains(normalizada) ? RespostaDoCliente.Negativa : RespostaDoCliente.Outra;
    }

    /// <summary>Lote vazio é <c>Outra</c>. Mistura de "sim" e "não", ou qualquer mensagem fora da lista, também.</summary>
    public static RespostaDoCliente ClassificarLote(IReadOnlyCollection<string?> mensagens)
    {
        if (mensagens.Count == 0)
            return RespostaDoCliente.Outra;

        var primeira = Classificar(mensagens.First());
        if (primeira == RespostaDoCliente.Outra)
            return RespostaDoCliente.Outra;

        return mensagens.All(m => Classificar(m) == primeira) ? primeira : RespostaDoCliente.Outra;
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
