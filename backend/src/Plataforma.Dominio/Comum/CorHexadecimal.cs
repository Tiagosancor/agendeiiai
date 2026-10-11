using System.Text.RegularExpressions;

namespace Plataforma.Dominio.Comum;

/// <summary>
/// Cor guardada para a interface: só <c>#RRGGBB</c>, sempre em minúsculas. Nada de nome de cor, <c>rgb()</c>, gradiente
/// ou variável CSS — o valor vai direto para um estilo na tela, então qualquer outra coisa é recusada.
/// </summary>
public static partial class CorHexadecimal
{
    /// <summary>Vazio (ou só espaços) = sem cor (nulo). Lança <see cref="ArgumentException"/> fora do formato.</summary>
    public static string? Normalizar(string? cor, string mensagemErro, string nomeParametro)
    {
        if (string.IsNullOrWhiteSpace(cor))
            return null;

        cor = cor.Trim();
        if (!Formato().IsMatch(cor))
            throw new ArgumentException(mensagemErro, nomeParametro);

        return cor.ToLowerInvariant();
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Formato();
}
