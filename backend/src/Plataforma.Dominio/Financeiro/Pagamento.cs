using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Financeiro;

/// <summary>Registro do pagamento de um atendimento (seção 7) — um por agendamento, associado quando o atendimento é concluído.</summary>
public class Pagamento : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid AgendamentoId { get; private set; }

    public decimal Valor { get; private set; }

    public FormaPagamento Forma { get; private set; }

    protected Pagamento()
    {
    }

    private Pagamento(Guid negocioId, Guid agendamentoId, decimal valor, FormaPagamento forma)
    {
        NegocioId = negocioId;
        AgendamentoId = agendamentoId;
        Valor = valor;
        Forma = forma;
    }

    public static Pagamento Criar(Guid negocioId, Guid agendamentoId, decimal valor, FormaPagamento forma)
    {
        if (valor <= 0)
            throw new ArgumentException("O valor do pagamento precisa ser maior que zero.", nameof(valor));

        return new Pagamento(negocioId, agendamentoId, valor, forma);
    }
}
