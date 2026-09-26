namespace Plataforma.Aplicacao.Abstracoes;

/// <summary>
/// Quem está fazendo a requisição no painel — preenchido a partir do JWT, junto com o
/// <see cref="IContextoNegocio"/>. Usado para autoria no log de auditoria e para as regras
/// "ninguém exclui o próprio usuário" (seção 7). Vazio fora do painel (rotas públicas, jobs).
/// </summary>
public interface IUsuarioAtual
{
    Guid? UsuarioId { get; }

    string? Email { get; }

    void Definir(Guid usuarioId, string? email);
}
