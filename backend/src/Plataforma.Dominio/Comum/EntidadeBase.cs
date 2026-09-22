namespace Plataforma.Dominio.Comum;

/// <summary>
/// Classe base para todas as entidades do domínio. Centraliza identidade e
/// carimbos de auditoria (criação/atualização), preenchidos pela infraestrutura.
/// </summary>
public abstract class EntidadeBase
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public DateTimeOffset CriadoEm { get; internal set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? AtualizadoEm { get; internal set; }
}
