using System.Text.RegularExpressions;

namespace Plataforma.Dominio.Comum;

/// <summary>
/// CPF validado e normalizado (só os 11 dígitos, sem máscara). Nunca guarde isto direto
/// no banco — sempre passe por <c>ICriptografiaCpf</c> antes (seção 8.4: CPF criptografado
/// em repouso). Este tipo só garante que o valor É um CPF válido antes de criptografar.
/// </summary>
public sealed partial class Cpf : IEquatable<Cpf>
{
    public string Digitos { get; }

    private Cpf(string digitos) => Digitos = digitos;

    public static Cpf Criar(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ArgumentException("O CPF não pode ser vazio.", nameof(valor));

        var digitos = SomenteDigitos().Replace(valor, string.Empty);

        if (digitos.Length != 11)
            throw new ArgumentException("O CPF precisa ter 11 dígitos.", nameof(valor));

        if (TodosOsDigitosIguais(digitos))
            throw new ArgumentException("CPF inválido.", nameof(valor));

        if (!DigitosVerificadoresConferem(digitos))
            throw new ArgumentException("CPF inválido.", nameof(valor));

        return new Cpf(digitos);
    }

    public static bool TentarCriar(string? valor, out Cpf? cpf)
    {
        cpf = null;
        if (string.IsNullOrWhiteSpace(valor))
            return false;

        try
        {
            cpf = Criar(valor);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Formato "***.456.789-**": esconde o primeiro grupo e os dígitos verificadores (seção 8.4).</summary>
    public string Mascarado() => $"***.{Digitos[3..6]}.{Digitos[6..9]}-**";

    public string ComPontuacao() => $"{Digitos[..3]}.{Digitos[3..6]}.{Digitos[6..9]}-{Digitos[9..]}";

    private static bool TodosOsDigitosIguais(string digitos) => digitos.Distinct().Count() == 1;

    private static bool DigitosVerificadoresConferem(string digitos)
    {
        var primeiroDigito = CalcularDigitoVerificador(digitos[..9], 10);
        var segundoDigito = CalcularDigitoVerificador(digitos[..9] + primeiroDigito, 11);

        return digitos[9] == primeiroDigito && digitos[10] == segundoDigito;
    }

    private static char CalcularDigitoVerificador(string baseDigitos, int pesoInicial)
    {
        var soma = 0;
        var peso = pesoInicial;

        foreach (var caractere in baseDigitos)
        {
            soma += (caractere - '0') * peso;
            peso--;
        }

        var resto = soma % 11;
        var digito = resto < 2 ? 0 : 11 - resto;

        return (char)('0' + digito);
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex SomenteDigitos();

    public bool Equals(Cpf? other) => other is not null && Digitos == other.Digitos;
    public override bool Equals(object? obj) => Equals(obj as Cpf);
    public override int GetHashCode() => Digitos.GetHashCode();
    public override string ToString() => Mascarado();
}
