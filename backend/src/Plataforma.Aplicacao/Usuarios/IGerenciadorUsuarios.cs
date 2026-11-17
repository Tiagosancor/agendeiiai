using Plataforma.Aplicacao.Cadastros;
using Plataforma.Dominio.Comum;
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

    /// <summary>Lança <see cref="IdentificadorIndisponivelException"/> (e-mail, nome de usuário ou telefone em uso) e <see cref="ErroDeAcessoException"/>.</summary>
    Task<Guid> CriarAsync(CriarUsuario dados, CancellationToken cancellationToken = default);

    /// <summary>Lança <see cref="IdentificadorIndisponivelException"/> se o novo e-mail já for de outro usuário.</summary>
    Task<bool> AtualizarDadosAsync(Guid usuarioId, AtualizarUsuario dados, CancellationToken cancellationToken = default);

    /// <summary>Substitui e-mail, nome de usuário e telefone de login. Lança <see cref="IdentificadorIndisponivelException"/> e <see cref="ErroDeAcessoException"/>.</summary>
    Task<bool> DefinirAcessoAsync(Guid usuarioId, DefinirAcessoUsuario dados, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Administrador define nova senha": força mínima (<c>senha_fraca</c>), revoga as sessões, marca "definida por terceiro"
    /// e audita. Na própria conta lança <see cref="OperacaoCadastroBloqueadaException"/> (use "Minha conta").
    /// </summary>
    Task<bool> AlterarSenhaAsync(Guid usuarioId, string novaSenha, CancellationToken cancellationToken = default);

    /// <summary>Envia o convite por e-mail; <c>convite_sem_email</c> (<see cref="ErroDeAcessoException"/>) se a conta não tem e-mail.</summary>
    Task<bool> EnviarConviteAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    Task<bool> ConcederPermissaoAsync(Guid usuarioId, Permissao permissao, CancellationToken cancellationToken = default);

    /// <summary>Lança <see cref="OperacaoCadastroBloqueadaException"/> ao tirar a gestão de usuários do último Administrador.</summary>
    Task<bool> RevogarPermissaoAsync(Guid usuarioId, Permissao permissao, CancellationToken cancellationToken = default);

    /// <summary>Lança <see cref="OperacaoCadastroBloqueadaException"/> para o último Administrador ou o próprio usuário.</summary>
    Task<bool> DesativarAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    Task<bool> AtivarAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    Task<PreviaExclusao?> ObterPreviaExclusaoAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Encerra todas as sessões do usuário na hora (seção 7).</summary>
    Task<ResultadoExclusao> ExcluirAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Decifra o CPF completo — só chame depois de checar a permissão de quem pediu.</summary>
    Task<string?> RevelarCpfAsync(Guid usuarioId, CancellationToken cancellationToken = default);
}

public sealed record UsuarioResumo(
    Guid Id, string Nome, string? Email, Perfil Perfil, bool Ativo, string? FotoUrl,
    bool EmailVerificado = false, string? NomeUsuario = null, string? TelefoneLogin = null);

public sealed record UsuarioDetalhe(
    Guid Id, string Nome, string? Email, string? Telefone, Perfil Perfil, bool Ativo, string? FotoUrl,
    string? CpfMascarado, IReadOnlyCollection<Permissao> Permissoes, Endereco? Endereco = null,
    bool EmailVerificado = false, string? NomeUsuario = null, string? TelefoneLogin = null);

/// <summary>
/// Pelo menos um entre <c>Email</c>, <c>NomeUsuario</c> e <c>TelefoneLogin</c> (400 <c>identificador_obrigatorio</c>). O e-mail
/// digitado nasce **não verificado**; o telefone de contato (<c>Telefone</c>) nunca vira login sozinho.
/// </summary>
public sealed record CriarUsuario(
    string Nome, string? Email, string Senha, Perfil Perfil,
    string? Telefone = null, string? Cpf = null, string? NomeUsuario = null, string? TelefoneLogin = null);

/// <summary>Substitui os três identificadores de uma vez; nulo ou vazio remove (desde que sobre outro).</summary>
public sealed record DefinirAcessoUsuario(string? Email, string? NomeUsuario, string? TelefoneLogin);

/// <summary>
/// Edição da ficha (seção 7). Campos nulos ficam como estão (compatível com quem só mandava
/// nome/telefone/CPF); <c>FotoUrl</c> vazia apaga a foto. <c>Email</c> nulo/vazio = não muda (para remover, <c>PUT .../acesso</c>).
/// </summary>
public sealed record AtualizarUsuario(
    string Nome, string? Telefone, string? Cpf, string? Email = null, Endereco? Endereco = null, string? FotoUrl = null);
