using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Administracao;

/// <summary>
/// Dono do produto (seção 7). Separado dos usuários dos negócios — nunca tem negócio, nunca
/// nasce pelo cadastro público: só pelo comando <c>criar-admin-plataforma</c>.
/// </summary>
public class AdministradorPlataforma : EntidadeBase
{
    public string Email { get; private set; } = string.Empty;

    public string Nome { get; private set; } = string.Empty;

    public string SenhaHash { get; private set; } = string.Empty;

    public bool Ativo { get; private set; } = true;

    protected AdministradorPlataforma()
    {
    }

    public static AdministradorPlataforma Criar(string email, string nome, string senhaHash)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("O e-mail é obrigatório.", nameof(email));

        if (string.IsNullOrWhiteSpace(senhaHash))
            throw new ArgumentException("A senha é obrigatória.", nameof(senhaHash));

        return new AdministradorPlataforma
        {
            Email = email.Trim().ToLowerInvariant(),
            Nome = string.IsNullOrWhiteSpace(nome) ? email.Trim() : nome.Trim(),
            SenhaHash = senhaHash,
        };
    }

    public void AlterarSenha(string novoHash)
    {
        if (string.IsNullOrWhiteSpace(novoHash))
            throw new ArgumentException("A senha é obrigatória.", nameof(novoHash));

        SenhaHash = novoHash;
    }
}
