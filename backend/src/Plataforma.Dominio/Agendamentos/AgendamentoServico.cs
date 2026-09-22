using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Agendamentos;

/// <summary>
/// Um serviço dentro de um agendamento — preço e duração são um retrato do momento da
/// reserva (mudar o preço do serviço depois não altera agendamentos já feitos).
/// </summary>
public class AgendamentoServico : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid AgendamentoId { get; private set; }

    public Guid ServicoId { get; private set; }

    public string Nome { get; private set; } = string.Empty;

    public decimal Preco { get; private set; }

    public int DuracaoMinutos { get; private set; }

    protected AgendamentoServico()
    {
    }

    internal AgendamentoServico(Guid negocioId, Guid agendamentoId, Guid servicoId, string nome, decimal preco, int duracaoMinutos)
    {
        NegocioId = negocioId;
        AgendamentoId = agendamentoId;
        ServicoId = servicoId;
        Nome = nome;
        Preco = preco;
        DuracaoMinutos = duracaoMinutos;
    }
}
