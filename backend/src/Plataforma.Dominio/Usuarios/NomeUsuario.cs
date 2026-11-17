using System.Text.RegularExpressions;

namespace Plataforma.Dominio.Usuarios;

/// <summary>Por que um texto não serve como nome de usuário.</summary>
public enum ProblemaNomeUsuario
{
    Nenhum = 0,
    Invalido = 1,
    Reservado = 2,
}

/// <summary>
/// Nome de usuário para entrar no painel (alternativa ao e-mail e ao telefone). Só ASCII <c>[a-z0-9._-]</c>, 3 a 30
/// caracteres, começa e termina com letra ou número, sem dois sinais seguidos e <b>com pelo menos uma letra</b>: um nome
/// só de dígitos seria confundido com telefone no login (<see cref="IdentificadorLogin"/>). Sem <c>@</c> nem <c>+</c>,
/// o que garante a desambiguação com e-mail e telefone. Função pura — o nome do produto como reservado é conferido na
/// Infraestrutura (o domínio não conhece a marca).
/// </summary>
public sealed partial class NomeUsuario : IEquatable<NomeUsuario>
{
    public const int TamanhoMinimo = 3;
    public const int TamanhoMaximo = 30;

    public static readonly IReadOnlyCollection<string> Reservados = new[]
    {
        "admin", "administrador", "root", "suporte", "sistema", "plataforma", "api", "app", "www",
        "painel", "conta", "login", "null", "undefined", "eu", "me",
    };

    public string Valor { get; }

    private NomeUsuario(string valor) => Valor = valor;

    /// <summary>Normalização do texto digitado (a mesma da criação e do login).</summary>
    public static string Normalizar(string? valor) => (valor ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Avalia sem lançar. Reservados extras (ex.: nome do produto) vêm de quem chama, já normalizados.</summary>
    public static ProblemaNomeUsuario Avaliar(string? valor, IEnumerable<string>? reservadosExtras = null)
    {
        var normalizado = Normalizar(valor);

        if (normalizado.Length is < TamanhoMinimo or > TamanhoMaximo
            || !FormatoValido().IsMatch(normalizado)
            || !normalizado.Any(char.IsAsciiLetter)
            || SinaisSeguidos().IsMatch(normalizado))
            return ProblemaNomeUsuario.Invalido;

        if (Reservados.Contains(normalizado) || (reservadosExtras?.Contains(normalizado) ?? false))
            return ProblemaNomeUsuario.Reservado;

        return ProblemaNomeUsuario.Nenhum;
    }

    public static NomeUsuario Criar(string valor, IEnumerable<string>? reservadosExtras = null) =>
        Avaliar(valor, reservadosExtras) switch
        {
            ProblemaNomeUsuario.Nenhum => new NomeUsuario(Normalizar(valor)),
            ProblemaNomeUsuario.Reservado => throw new ArgumentException("Esse nome de usuário é reservado.", nameof(valor)),
            _ => throw new ArgumentException(
                "O nome de usuário deve ter de 3 a 30 caracteres (letras, números, ponto, hífen e sublinhado), com pelo menos uma letra.",
                nameof(valor)),
        };

    public static bool TentarCriar(string? valor, out NomeUsuario? nomeUsuario, IEnumerable<string>? reservadosExtras = null)
    {
        nomeUsuario = Avaliar(valor, reservadosExtras) == ProblemaNomeUsuario.Nenhum ? new NomeUsuario(Normalizar(valor)) : null;
        return nomeUsuario is not null;
    }

    /// <summary>
    /// Só para ler do banco: não revalida (um nome gravado quando ainda não era reservado não pode derrubar a leitura).
    /// </summary>
    public static NomeUsuario Reconstituir(string valorDoBanco) => new(valorDoBanco);

    // O tamanho e o fim/início sem sinal ficam aqui; "pelo menos uma letra" e "sem sinais seguidos" nas verificações acima.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*[a-z0-9]$")]
    private static partial Regex FormatoValido();

    [GeneratedRegex(@"[._-]{2}")]
    private static partial Regex SinaisSeguidos();

    public bool Equals(NomeUsuario? other) => other is not null && Valor == other.Valor;
    public override bool Equals(object? obj) => Equals(obj as NomeUsuario);
    public override int GetHashCode() => Valor.GetHashCode();
    public override string ToString() => Valor;
}
