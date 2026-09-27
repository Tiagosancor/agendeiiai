namespace Plataforma.Aplicacao.Autenticacao;

/// <summary>
/// "Esqueci minha senha" do painel: vale para qualquer usuário de negócio (o dono que
/// assinou o plano e os usuários que ele cadastrou). O link vai para o e-mail da conta.
/// </summary>
public interface IServicoRedefinicaoSenha
{
    /// <summary>
    /// Envia o link se o e-mail for de uma conta ativa. Não diz se a conta existe — quem
    /// chama responde sempre a mesma coisa (anti-enumeração, mesmo espírito da seção 8.1.3).
    /// </summary>
    Task SolicitarAsync(string email, CancellationToken cancellationToken = default);

    Task<ResultadoRedefinicaoSenha> RedefinirAsync(string token, string novaSenha, CancellationToken cancellationToken = default);
}

public enum ResultadoRedefinicaoSenha
{
    Sucesso,
    LinkInvalido,
    SenhaFraca,
}
