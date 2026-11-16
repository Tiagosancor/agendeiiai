using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Usuarios;

public enum TipoIdentificadorLogin
{
    Invalido = 0,
    Email = 1,
    Telefone = 2,
    NomeUsuario = 3,
}

/// <summary>
/// O que a pessoa digitou no campo único do login, classificado: e-mail (tem <c>@</c>), telefone (só <c>+</c> opcional e
/// dígitos depois de tirar espaço, parêntese, traço e ponto) ou nome de usuário. Inválido não é erro de requisição: segue o
/// mesmo caminho do "não encontrado". Função pura. O telefone sai na forma canônica (13 dígitos), a que fica no banco.
/// </summary>
public sealed record IdentificadorLogin(
    TipoIdentificadorLogin Tipo, string? Email, TelefoneE164? Telefone, NomeUsuario? NomeUsuario)
{
    public static IdentificadorLogin Invalido { get; } = new(TipoIdentificadorLogin.Invalido, null, null, null);

    public static IdentificadorLogin Interpretar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return Invalido;

        var bruto = texto.Trim();

        if (bruto.Contains('@'))
            return new(TipoIdentificadorLogin.Email, bruto.ToLowerInvariant(), null, null);

        var semPontuacao = new string(bruto.Where(c => c is not (' ' or '(' or ')' or '-' or '.')).ToArray());
        var corpo = semPontuacao.StartsWith('+') ? semPontuacao[1..] : semPontuacao;

        if (corpo.Length > 0 && corpo.All(char.IsAsciiDigit))
            return TelefoneE164.TentarCriar(bruto, out var telefone) && telefone is not null
                ? new(TipoIdentificadorLogin.Telefone, null, telefone.FormaCanonica(), null)
                : Invalido;

        return NomeUsuario.TentarCriar(bruto, out var nome) && nome is not null
            ? new(TipoIdentificadorLogin.NomeUsuario, null, null, nome)
            : Invalido;
    }
}
