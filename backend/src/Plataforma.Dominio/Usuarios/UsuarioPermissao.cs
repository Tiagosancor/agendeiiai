using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Usuarios;

/// <summary>Concessão de uma permissão a um usuário específico (seção 10: <c>UsuarioPermissao</c>).</summary>
public class UsuarioPermissao : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid UsuarioId { get; private set; }

    public Permissao Permissao { get; private set; }

    protected UsuarioPermissao()
    {
    }

    internal UsuarioPermissao(Guid negocioId, Guid usuarioId, Permissao permissao)
    {
        NegocioId = negocioId;
        UsuarioId = usuarioId;
        Permissao = permissao;
    }
}
