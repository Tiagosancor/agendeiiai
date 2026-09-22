using System.Text.RegularExpressions;

namespace Plataforma.Dominio.Comum;

/// <summary>
/// Telefone normalizado para E.164 (<c>+DDI...</c>, só dígitos depois do <c>+</c>).
/// É a chave de identificação do cliente dentro do negócio (seção 7). Validação
/// propositalmente simples (formato, não o plano de numeração de cada país) — uma
/// validação completa (libphonenumber) fica para quando for necessário de verdade.
/// </summary>
public sealed partial class TelefoneE164 : IEquatable<TelefoneE164>
{
    public string Valor { get; }

    private TelefoneE164(string valor) => Valor = valor;

    public static TelefoneE164 Criar(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ArgumentException("O telefone não pode ser vazio.", nameof(valor));

        var normalizado = valor.Trim();

        if (!FormatoValido().IsMatch(normalizado))
            throw new ArgumentException("O telefone precisa estar em E.164 (ex.: +5571988887777).", nameof(valor));

        return new TelefoneE164(normalizado);
    }

    public static bool TentarCriar(string? valor, out TelefoneE164? telefone)
    {
        telefone = null;
        if (string.IsNullOrWhiteSpace(valor))
            return false;

        try
        {
            telefone = Criar(valor);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Mascarado para logs (seção 8.1.6) — mantém DDI+DDD e os 4 últimos dígitos, esconde o meio.</summary>
    public string Mascarado()
    {
        var digitos = Valor[1..]; // sem o '+'
        if (digitos.Length <= 8)
            return "+****";

        var inicio = digitos[..4];
        var fim = digitos[^4..];
        return $"+{inicio}****{fim}";
    }

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex FormatoValido();

    public bool Equals(TelefoneE164? other) => other is not null && Valor == other.Valor;
    public override bool Equals(object? obj) => Equals(obj as TelefoneE164);
    public override int GetHashCode() => Valor.GetHashCode();
    public override string ToString() => Valor;

    public static implicit operator string(TelefoneE164 telefone) => telefone.Valor;
}
