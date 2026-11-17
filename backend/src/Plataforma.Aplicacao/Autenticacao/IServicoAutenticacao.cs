namespace Plataforma.Aplicacao.Autenticacao;

/// <summary>
/// Login e renovação do painel. O identificador (e-mail, telefone ou nome de usuário) é a chave, global e não por negócio
/// (ver docs/decisoes.md): o painel fica todo em <c>app.{dominio}</c>, então o identificador sozinho precisa apontar para o
/// negócio certo antes de qualquer token existir.
/// </summary>
public interface IServicoAutenticacao
{
    /// <summary>
    /// <paramref name="identificador"/> é e-mail, telefone ou nome de usuário (<c>IdentificadorLogin</c>). Inexistente, inválido,
    /// inativo e senha errada dão o mesmo <see cref="ResultadoLogin.Falha"/> (e sempre passam pelo bcrypt); identificador
    /// bloqueado por excesso de falhas dá <see cref="ResultadoLogin.TentativasEsgotadas"/>, sem conferir a senha.
    /// </summary>
    Task<ResultadoLogin> LoginAsync(string identificador, string senha, CancellationToken cancellationToken = default);

    /// <summary>Troca um refresh token válido por um novo par (rotação — seção 4).</summary>
    Task<ResultadoLogin> RenovarAsync(string refreshTokenBruto, CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshTokenBruto, CancellationToken cancellationToken = default);
}

public sealed record ResultadoLogin(
    bool Sucesso,
    string? AccessToken = null,
    DateTimeOffset? AccessTokenExpiraEm = null,
    string? RefreshToken = null,
    DateTimeOffset? RefreshTokenExpiraEm = null,
    bool Bloqueado = false)
{
    public static ResultadoLogin Falha { get; } = new(false);

    /// <summary>Identificador com falhas demais na janela (429 <c>muitas_tentativas</c>).</summary>
    public static ResultadoLogin TentativasEsgotadas { get; } = new(false, Bloqueado: true);
}
