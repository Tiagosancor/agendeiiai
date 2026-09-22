using Plataforma.Dominio.Comum;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>
/// Entidade só de teste, usada para provar o mecanismo do *global query filter*
/// multi-tenant do <c>PlataformaDbContext</c> sem depender de nenhuma entidade de
/// negócio real (a Sprint 0 ainda não tem nenhuma).
/// </summary>
public sealed class ItemDeTeste : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public string Nome { get; private set; } = string.Empty;

    private ItemDeTeste()
    {
    }

    public static ItemDeTeste Criar(Guid negocioId, string nome) => new() { NegocioId = negocioId, Nome = nome };
}
