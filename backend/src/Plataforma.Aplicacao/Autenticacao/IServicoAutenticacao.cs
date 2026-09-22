namespace Plataforma.Aplicacao.Autenticacao;

/// <summary>
/// Login e renovação do painel. O e-mail é a chave (global, não por negócio — ver
/// docs/decisoes.md): o painel fica todo em <c>app.{dominio}</c>, então é o e-mail sozinho
/// que precisa apontar para o negócio certo antes de qualquer token existir.
/// </summary>
public interface IServicoAutenticacao
{
    Task<ResultadoLogin> LoginAsync(string email, string senha, CancellationToken cancellationToken = default);

    /// <summary>Troca um refresh token válido por um novo par (rotação — seção 4).</summary>
    Task<ResultadoLogin> RenovarAsync(string refreshTokenBruto, CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshTokenBruto, CancellationToken cancellationToken = default);
}

public sealed record ResultadoLogin(
    bool Sucesso,
    string? AccessToken = null,
    DateTimeOffset? AccessTokenExpiraEm = null,
    string? RefreshToken = null,
    DateTimeOffset? RefreshTokenExpiraEm = null)
{
    public static ResultadoLogin Falha { get; } = new(false);
}
