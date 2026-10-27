namespace Plataforma.Dominio.WhatsAppAgente;

/// <summary>
/// Regra única do nome que o cliente digita na conversa do WhatsApp (e que vira cadastro e texto de notificação): 2 a 80
/// caracteres, só letras (com acento), espaço, apóstrofo, hífen e ponto; sem dígitos, "&lt;", "&gt;", "@", "/" ou ":" nem
/// endereço de site (<c>www</c>, <c>http</c>, ou "palavra.palavra" colada, como <c>golpe.com</c>). Usada pelo serviço de
/// agendamento do agente e pela ferramenta da Fatia 3, para a recusa não depender de quem chama.
/// </summary>
public static class NomeClienteWhatsApp
{
    public const int TamanhoMinimo = 2;
    public const int TamanhoMaximo = PendenciaAgendamentoWhatsApp.TamanhoMaximoNome;

    /// <summary>Normaliza (apara e junta espaços repetidos) e valida. <paramref name="nome"/> só vale quando devolve verdadeiro.</summary>
    public static bool TentarNormalizar(string? bruto, out string nome)
    {
        nome = string.Empty;
        if (string.IsNullOrWhiteSpace(bruto))
            return false;

        var candidato = string.Join(' ', bruto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (candidato.Length is < TamanhoMinimo or > TamanhoMaximo)
            return false;

        foreach (var c in candidato)
        {
            if (!(char.IsLetter(c) || c is ' ' or '\'' or '’' or '-' or '.'))
                return false;
        }

        if (!candidato.Any(char.IsLetter))
            return false;

        if (candidato.Contains("www", StringComparison.OrdinalIgnoreCase)
            || candidato.Contains("http", StringComparison.OrdinalIgnoreCase)
            || ParecePalavraComPontoColado(candidato))
            return false;

        nome = candidato;
        return true;
    }

    public static bool EhValido(string? bruto) => TentarNormalizar(bruto, out _);

    // "golpe.com", "x.ai": letra, ponto e pelo menos duas letras coladas. Abreviação com espaço ("J. Silva", "Ana M.") passa.
    private static bool ParecePalavraComPontoColado(string texto)
    {
        for (var i = 1; i < texto.Length - 2; i++)
        {
            if (texto[i] == '.' && char.IsLetter(texto[i - 1]) && char.IsLetter(texto[i + 1]) && char.IsLetter(texto[i + 2]))
                return true;
        }

        return false;
    }
}
