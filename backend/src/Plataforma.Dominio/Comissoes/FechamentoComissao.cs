using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Comissoes;

/// <summary>
/// Retrato do acerto de um profissional numa quinzena fechada (seção 7). Gravado ao fechar e
/// nunca alterado depois; reabrir a quinzena apaga os fechamentos dela, e fechar de novo gera
/// outros. Guarda também a comissão de produto e o vale/consumo descontados (item 12); "marcar como pago" ainda não.
/// </summary>
public class FechamentoComissao : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid PeriodoComissaoId { get; private set; }

    public Guid ProfissionalId { get; private set; }

    public decimal TotalCobrado { get; private set; }

    public decimal TotalComissao { get; private set; }

    public int QuantidadeServicos { get; private set; }

    /// <summary>Comissão sobre as vendas de produto do período em que ele foi o vendedor (separada da de serviço).</summary>
    public decimal TotalComissaoProdutos { get; private set; }

    /// <summary>Vales descontados neste fechamento.</summary>
    public decimal TotalVales { get; private set; }

    /// <summary>Consumo interno descontado neste fechamento.</summary>
    public decimal TotalConsumo { get; private set; }

    /// <summary>A receber: comissão de serviço + de produto − vales − consumo descontados. Nunca negativo.</summary>
    public decimal Liquido { get; private set; }

    /// <summary>Saldo devedor que não coube neste fechamento e fica para a próxima quinzena (retrato do momento).</summary>
    public decimal SaldoRestante { get; private set; }

    public Guid? FechadoPorUsuarioId { get; private set; }

    public DateTimeOffset FechadoEm { get; private set; }

    protected FechamentoComissao()
    {
    }

    private FechamentoComissao(Guid negocioId, Guid periodoId, Guid profissionalId)
    {
        NegocioId = negocioId;
        PeriodoComissaoId = periodoId;
        ProfissionalId = profissionalId;
    }

    public static FechamentoComissao Criar(
        Guid negocioId, Guid periodoId, Guid profissionalId, decimal totalCobrado, decimal totalComissao, int quantidadeServicos,
        Guid? fechadoPorUsuarioId, DateTimeOffset fechadoEm) =>
        new(negocioId, periodoId, profissionalId)
        {
            TotalCobrado = totalCobrado,
            TotalComissao = totalComissao,
            QuantidadeServicos = quantidadeServicos,
            FechadoPorUsuarioId = fechadoPorUsuarioId,
            FechadoEm = fechadoEm,
            Liquido = totalComissao,
        };

    /// <summary>Comissão de produto e saldo devedor (vale e consumo) descontado neste fechamento (seção 7).</summary>
    public void RegistrarProdutosEDescontos(decimal comissaoProdutos, decimal vales, decimal consumo, decimal saldoRestante)
    {
        TotalComissaoProdutos = comissaoProdutos;
        TotalVales = vales;
        TotalConsumo = consumo;
        Liquido = Math.Max(0m, TotalComissao + comissaoProdutos - vales - consumo);
        SaldoRestante = saldoRestante;
    }

    /// <summary>
    /// Um lançamento já quitado aqui foi editado ou excluído (com confirmação): o desconto sai e o líquido deste
    /// fechamento passado sobe na mesma medida.
    /// </summary>
    public void EstornarDesconto(TipoLancamentoSaldo tipo, decimal valor)
    {
        if (tipo == TipoLancamentoSaldo.Vale)
            TotalVales -= valor;
        else
            TotalConsumo -= valor;
        Liquido += valor;
    }
}
