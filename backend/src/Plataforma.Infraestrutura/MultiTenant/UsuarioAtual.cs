using Plataforma.Aplicacao.Abstracoes;

namespace Plataforma.Infraestrutura.MultiTenant;

public sealed class UsuarioAtual : IUsuarioAtual
{
    public Guid? UsuarioId { get; private set; }

    public string? Email { get; private set; }

    public void Definir(Guid usuarioId, string? email)
    {
        UsuarioId = usuarioId;
        Email = email;
    }
}
