using System.Text.RegularExpressions;

namespace Plataforma.Dominio.Negocios;

/// <summary>
/// Identificador único do negócio usado como subdomínio público (<c>https://{slug}.{dominio}</c>).
/// Regras da seção 5: 3 a 30 caracteres <c>[a-z0-9-]</c>, único, fora da lista de reservados.
/// </summary>
public sealed partial class Slug : IEquatable<Slug>
{
    public const int TamanhoMinimo = 3;
    public const int TamanhoMaximo = 30;

    /// <summary>Subdomínios reservados pela plataforma — nunca podem ser usados por um negócio.</summary>
    public static readonly IReadOnlyCollection<string> Reservados = new[]
    {
        "www", "app", "api", "admin", "mail", "static", "cdn",
        "assets", "blog", "docs", "help", "support", "status",
        "ftp", "smtp", "ns1", "ns2", "webmail", "cpanel",
        "autoconfig", "autodiscover", "painel",
    };

    public string Valor { get; }

    private Slug(string valor) => Valor = valor;

    public static Slug Criar(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ArgumentException("O slug não pode ser vazio.", nameof(valor));

        var normalizado = valor.Trim().ToLowerInvariant();

        if (normalizado.Length is < TamanhoMinimo or > TamanhoMaximo)
            throw new ArgumentException(
                $"O slug deve ter entre {TamanhoMinimo} e {TamanhoMaximo} caracteres.", nameof(valor));

        if (!FormatoValido().IsMatch(normalizado))
            throw new ArgumentException(
                "O slug só pode conter letras minúsculas, números e hífen, sem começar ou terminar com hífen.",
                nameof(valor));

        if (Reservados.Contains(normalizado))
            throw new ArgumentException($"O slug '{normalizado}' é reservado e não pode ser usado.", nameof(valor));

        return new Slug(normalizado);
    }

    /// <summary>Tenta interpretar um segmento de host (já em minúsculas) como slug, sem lançar exceção.</summary>
    public static bool TentarCriar(string? valor, out Slug? slug)
    {
        slug = null;
        if (string.IsNullOrWhiteSpace(valor))
            return false;

        try
        {
            slug = Criar(valor);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex FormatoValido();

    public bool Equals(Slug? other) => other is not null && Valor == other.Valor;
    public override bool Equals(object? obj) => Equals(obj as Slug);
    public override int GetHashCode() => Valor.GetHashCode();
    public override string ToString() => Valor;

    public static implicit operator string(Slug slug) => slug.Valor;
}
