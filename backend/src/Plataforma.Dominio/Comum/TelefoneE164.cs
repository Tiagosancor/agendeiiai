using System.Text.RegularExpressions;

namespace Plataforma.Dominio.Comum;

/// <summary>
/// Telefone normalizado para E.164 (<c>+DDI...</c>, só dígitos depois do <c>+</c>).
/// É a chave de identificação do cliente dentro do negócio (seção 7). Validação
/// propositalmente simples (formato, não o plano de numeração de cada país) — uma
/// validação completa (libphonenumber) fica para quando for necessário de verdade.
/// <para>
/// Aceita também o jeito brasileiro de escrever (quase ninguém digita o <c>+55</c>): sem <c>+</c>,
/// 10 ou 11 dígitos são DDD + número, e o <c>55</c> é completado; espaço, parêntese, traço e
/// ponto são ignorados; um <c>0</c> de discagem na frente do DDD também. Com <c>+</c>, vale o que
/// foi digitado (número de outro país, que precisa do <c>+</c>).
/// </para>
/// </summary>
public sealed partial class TelefoneE164 : IEquatable<TelefoneE164>
{
    public string Valor { get; }

    private TelefoneE164(string valor) => Valor = valor;

    public static TelefoneE164 Criar(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ArgumentException("O telefone não pode ser vazio.", nameof(valor));

        var normalizado = Normalizar(valor);

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

    private static string Normalizar(string valor)
    {
        var texto = valor.Trim();
        var internacional = texto.StartsWith('+');

        // Tira só a pontuação comum; qualquer outro caractere (letra etc.) fica, para o formato
        // recusar em vez de "consertar" um valor inválido.
        var digitos = new string(texto.Where(c => c is not (' ' or '(' or ')' or '-' or '.' or '+')).ToArray());

        if (internacional)
            return "+" + digitos;

        // "071 98888-7777": o 0 de discagem interurbana não faz parte do número.
        if (digitos.Length is 11 or 12 && digitos.StartsWith('0'))
            digitos = digitos[1..];

        if (digitos.Length is 10 or 11)
            return "+55" + digitos;

        // Já com o 55 na frente (sem o '+'). Qualquer outra coisa sem '+' (ex.: número sem DDD)
        // fica sem o '+' de propósito, para o formato recusar — número de outro país precisa do '+'.
        return digitos.Length is 12 or 13 && digitos.StartsWith("55") ? "+" + digitos : digitos;
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
