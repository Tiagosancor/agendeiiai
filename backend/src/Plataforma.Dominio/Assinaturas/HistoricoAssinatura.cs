using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Assinaturas;

/// <summary>Uma transição da assinatura (seção 7): quem, quando (<c>CriadoEm</c>), de qual para qual estado e por quê.</summary>
public class HistoricoAssinatura : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid AssinaturaId { get; private set; }

    /// <summary>Nulo na criação da assinatura.</summary>
    public EstadoAssinatura? EstadoAnterior { get; private set; }

    public EstadoAssinatura EstadoNovo { get; private set; }

    public string Autor { get; private set; } = string.Empty;

    public string Motivo { get; private set; } = string.Empty;

    protected HistoricoAssinatura()
    {
    }

    internal HistoricoAssinatura(
        Guid negocioId, Guid assinaturaId, EstadoAssinatura? estadoAnterior, EstadoAssinatura estadoNovo,
        string autor, string motivo, DateTimeOffset em)
    {
        NegocioId = negocioId;
        AssinaturaId = assinaturaId;
        EstadoAnterior = estadoAnterior;
        EstadoNovo = estadoNovo;
        Autor = autor;
        Motivo = motivo;
        CriadoEm = em;
    }
}
