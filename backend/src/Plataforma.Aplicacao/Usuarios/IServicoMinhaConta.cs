using Plataforma.Dominio.Usuarios;

namespace Plataforma.Aplicacao.Usuarios;

/// <summary>
/// "Minha conta" (qualquer usuário logado, mesmo com a assinatura suspensa): o usuário vem **só** do token, nunca de id na rota.
/// Erros: <see cref="ErroDeAcessoException"/> (400), <see cref="IdentificadorIndisponivelException"/> (409),
/// <see cref="MuitasTentativasException"/> (429).
/// </summary>
public interface IServicoMinhaConta
{
    Task<MinhaConta?> ObterAsync(CancellationToken cancellationToken = default);

    /// <summary>Troca (ou remove, com nulo) o nome de usuário; exige a senha atual.</summary>
    Task AlterarNomeUsuarioAsync(string? nomeUsuario, string senhaAtual, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pede a confirmação de um e-mail: envia um código de 6 dígitos ao endereço. Resposta igual em todos os casos (o e-mail já
    /// ser de outra conta, limite estourado, etc. não aparece): quem chama devolve 202 sempre que a senha atual confere.
    /// </summary>
    Task SolicitarEmailAsync(string email, string senhaAtual, CancellationToken cancellationToken = default);

    /// <summary>Confirma o código. Errado, vencido, esgotado ou perdido numa corrida: <c>codigo_invalido</c> (neutro).</summary>
    Task ConfirmarEmailAsync(string codigo, CancellationToken cancellationToken = default);

    /// <summary>Troca a senha; revoga todas as sessões (o front entra de novo com a senha nova).</summary>
    Task AlterarSenhaAsync(string senhaAtual, string novaSenha, CancellationToken cancellationToken = default);
}

public sealed record MinhaConta(
    string Nome, Perfil Perfil, string? Email, bool EmailVerificado, string? EmailPendenteMascarado,
    string? NomeUsuario, string? TelefoneLogin, bool SenhaDefinidaPorTerceiro);
