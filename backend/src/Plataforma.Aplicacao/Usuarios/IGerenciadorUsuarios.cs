using Plataforma.Dominio.Usuarios;

namespace Plataforma.Aplicacao.Usuarios;

/// <summary>
/// CRUD de usuários e permissões (seção 7). As implementações pegam o negócio do
/// <c>IContextoNegocio</c> internamente — nunca recebem <c>negocioId</c> por parâmetro,
/// exatamente para que seja impossível um endpoint do painel operar em outro negócio
/// além do que o JWT autorizou (seção 8.3.2).
/// </summary>
public interface IGerenciadorUsuarios
{
    Task<IReadOnlyList<UsuarioResumo>> ListarAsync(CancellationToken cancellationToken = default);

    Task<UsuarioDetalhe?> ObterAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Lança <see cref="EmailJaCadastradoException"/> se o e-mail já existir (é único globalmente).</summary>
    Task<Guid> CriarAsync(CriarUsuario dados, CancellationToken cancellationToken = default);

    Task<bool> AtualizarDadosAsync(Guid usuarioId, AtualizarUsuario dados, CancellationToken cancellationToken = default);

    Task<bool> AlterarSenhaAsync(Guid usuarioId, string novaSenha, CancellationToken cancellationToken = default);

    Task<bool> ConcederPermissaoAsync(Guid usuarioId, Permissao permissao, CancellationToken cancellationToken = default);

    Task<bool> RevogarPermissaoAsync(Guid usuarioId, Permissao permissao, CancellationToken cancellationToken = default);

    Task<bool> DesativarAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    Task<bool> AtivarAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Decifra o CPF completo — só chame depois de checar a permissão de quem pediu.</summary>
    Task<string?> RevelarCpfAsync(Guid usuarioId, CancellationToken cancellationToken = default);
}

public sealed record UsuarioResumo(Guid Id, string Nome, string Email, Perfil Perfil, bool Ativo, string? FotoUrl);

public sealed record UsuarioDetalhe(
    Guid Id, string Nome, string Email, string? Telefone, Perfil Perfil, bool Ativo, string? FotoUrl,
    string? CpfMascarado, IReadOnlyCollection<Permissao> Permissoes);

public sealed record CriarUsuario(
    string Nome, string Email, string Senha, Perfil Perfil,
    string? Telefone = null, string? Cpf = null);

public sealed record AtualizarUsuario(string Nome, string? Telefone, string? Cpf);

public sealed class EmailJaCadastradoException(string email)
    : Exception($"Já existe um usuário com o e-mail '{email}'.");
