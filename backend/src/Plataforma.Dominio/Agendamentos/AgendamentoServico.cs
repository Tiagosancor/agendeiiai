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

    // Comissão (seção 7), gravada na conclusão e nula em qualquer outro estado. É um retrato,
    // como o preço: mudar o percentual do profissional depois não mexe aqui.

    /// <summary>Quem recebe a comissão desta linha (o profissional do atendimento na conclusão).</summary>
    public Guid? ComissaoProfissionalId { get; private set; }

    /// <summary>Valor efetivamente cobrado pela linha: preço menos a parte dela no desconto do cupom.</summary>
    public decimal? ComissaoValorBase { get; private set; }

    public decimal? ComissaoPercentual { get; private set; }

    public decimal? ComissaoValor { get; private set; }

    public DateTimeOffset? ComissaoCalculadaEm { get; private set; }

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

    internal void RegistrarComissao(Guid profissionalId, decimal valorBase, decimal percentual, decimal valor, DateTimeOffset agora)
    {
        ComissaoProfissionalId = profissionalId;
        ComissaoValorBase = valorBase;
        ComissaoPercentual = percentual;
        ComissaoValor = valor;
        ComissaoCalculadaEm = agora;
    }

    internal void EstornarComissao()
    {
        ComissaoProfissionalId = null;
        ComissaoValorBase = null;
        ComissaoPercentual = null;
        ComissaoValor = null;
        ComissaoCalculadaEm = null;
    }
}
