using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Fidelidade;

/// <summary>Um selo do cartão de fidelidade (seção 7/10) — um por atendimento concluído; <see cref="Resgatado"/> marca quando consumido numa recompensa.</summary>
public class SeloCliente : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid ClienteId { get; private set; }

    public Guid AgendamentoId { get; private set; }

    public bool Resgatado { get; private set; }

    protected SeloCliente()
    {
    }

    private SeloCliente(Guid negocioId, Guid clienteId, Guid agendamentoId)
    {
        NegocioId = negocioId;
        ClienteId = clienteId;
        AgendamentoId = agendamentoId;
    }

    public static SeloCliente Criar(Guid negocioId, Guid clienteId, Guid agendamentoId) =>
        new(negocioId, clienteId, agendamentoId);

    public void MarcarResgatado() => Resgatado = true;
}
