using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Usuarios;

/// <summary>
/// Refresh token do login do painel. Guardado só como hash (mesmo padrão do código de
/// confirmação — seção 8.1.2), com rotação: cada uso gera um token novo e revoga este.
/// </summary>
public class TokenAtualizacao : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid UsuarioId { get; private set; }

    public string Hash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiraEm { get; private set; }

    public DateTimeOffset? RevogadoEm { get; private set; }

    public bool Valido => RevogadoEm is null && ExpiraEm > DateTimeOffset.UtcNow;

    protected TokenAtualizacao()
    {
    }

    private TokenAtualizacao(Guid negocioId, Guid usuarioId, string hash, DateTimeOffset expiraEm)
    {
        NegocioId = negocioId;
        UsuarioId = usuarioId;
        Hash = hash;
        ExpiraEm = expiraEm;
    }

    public static TokenAtualizacao Criar(Guid negocioId, Guid usuarioId, string hash, DateTimeOffset expiraEm) =>
        new(negocioId, usuarioId, hash, expiraEm);

    public void Revogar() => RevogadoEm ??= DateTimeOffset.UtcNow;
}
