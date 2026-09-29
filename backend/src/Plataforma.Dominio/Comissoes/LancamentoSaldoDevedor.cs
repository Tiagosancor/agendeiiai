using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Estoque;

namespace Plataforma.Dominio.Comissoes;

public enum TipoLancamentoSaldo
{
    /// <summary>Adiantamento em dinheiro.</summary>
    Vale = 1,

    /// <summary>Produto do estoque usado pelo profissional.</summary>
    ConsumoInterno = 2,
}

/// <summary>
/// Saldo devedor do profissional (seção 7): vale ou consumo interno. Desconta do valor a receber dele, nunca do
/// faturamento. Fica em aberto até ser quitado no fechamento da quinzena (<see cref="QuitacaoSaldo"/>), que pode
/// cobrir só uma parte — o resto continua em aberto para a próxima.
/// </summary>
public class LancamentoSaldoDevedor : EntidadeBase, IEntidadeDoNegocio
{
    public const int TamanhoMaximoDescricao = 500;

    public Guid NegocioId { get; private set; }

    public Guid ProfissionalId { get; private set; }

    public TipoLancamentoSaldo Tipo { get; private set; }

    public decimal Valor { get; private set; }

    /// <summary>Dia do lançamento no fuso do negócio: entra na quitação das quinzenas que terminam nele ou depois.</summary>
    public DateOnly Data { get; private set; }

    /// <summary>Motivo do vale ou observação do consumo (opcional).</summary>
    public string? Descricao { get; private set; }

    public Guid? ProdutoId { get; private set; }

    public string? NomeProduto { get; private set; }

    public int? Quantidade { get; private set; }

    public decimal? ValorUnitario { get; private set; }

    public Guid? MovimentoEstoqueId { get; private set; }

    public Guid? LancadoPorUsuarioId { get; private set; }

    protected LancamentoSaldoDevedor()
    {
    }

    public static LancamentoSaldoDevedor CriarVale(
        Guid negocioId, Guid profissionalId, decimal valor, DateOnly data, string? motivo, Guid? usuarioId)
    {
        var lancamento = new LancamentoSaldoDevedor
        {
            NegocioId = negocioId,
            ProfissionalId = profissionalId,
            Tipo = TipoLancamentoSaldo.Vale,
            LancadoPorUsuarioId = usuarioId,
        };
        lancamento.AlterarVale(valor, data, motivo);
        return lancamento;
    }

    /// <summary>Baixa o estoque (produto já travado pelo chamador) e lança o valor como saldo devedor do profissional.</summary>
    public static (LancamentoSaldoDevedor Lancamento, MovimentoEstoque Movimento) CriarConsumo(
        Guid profissionalId, Produto produto, int quantidade, decimal valorUnitario, DateOnly data, string? observacao, Guid? usuarioId,
        DateTimeOffset agora)
    {
        if (!produto.Ativo)
            throw new ArgumentException($"{produto.Nome} está inativo.", nameof(produto));
        ValidarValor(valorUnitario, permiteZero: true);

        var movimento = produto.RegistrarSaida(TipoMovimentoEstoque.ConsumoInterno, quantidade, valorUnitario, observacao, usuarioId, agora);
        var lancamento = new LancamentoSaldoDevedor
        {
            NegocioId = produto.NegocioId,
            ProfissionalId = profissionalId,
            Tipo = TipoLancamentoSaldo.ConsumoInterno,
            Data = data,
            ProdutoId = produto.Id,
            NomeProduto = produto.Nome,
            Quantidade = quantidade,
            MovimentoEstoqueId = movimento.Id,
            LancadoPorUsuarioId = usuarioId,
        };
        lancamento.AlterarConsumo(valorUnitario, observacao);
        return (lancamento, movimento);
    }

    public void AlterarVale(decimal valor, DateOnly data, string? motivo)
    {
        if (Tipo != TipoLancamentoSaldo.Vale)
            throw new InvalidOperationException("Não é um vale.");
        ValidarValor(valor, permiteZero: false);
        Valor = decimal.Round(valor, 2);
        Data = data;
        Descricao = Limpar(motivo);
    }

    /// <summary>O valor por unidade (a quantidade é do estoque e não muda aqui).</summary>
    public void AlterarConsumo(decimal valorUnitario, string? observacao)
    {
        if (Tipo != TipoLancamentoSaldo.ConsumoInterno)
            throw new InvalidOperationException("Não é um consumo interno.");
        ValidarValor(valorUnitario, permiteZero: true);
        ValorUnitario = decimal.Round(valorUnitario, 2);
        Valor = ValorUnitario.Value * Quantidade!.Value;
        Descricao = Limpar(observacao);
    }

    private static void ValidarValor(decimal valor, bool permiteZero)
    {
        if (valor < 0 || (!permiteZero && valor == 0))
            throw new ArgumentException(permiteZero ? "O valor não pode ser negativo." : "Informe um valor maior que zero.", nameof(valor));
    }

    private static string? Limpar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        var limpo = texto.Trim();
        return limpo.Length > TamanhoMaximoDescricao ? limpo[..TamanhoMaximoDescricao] : limpo;
    }
}

/// <summary>Quanto de um lançamento foi descontado num fechamento de quinzena. Reabrir a quinzena apaga (volta a ficar em aberto).</summary>
public class QuitacaoSaldo : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid FechamentoComissaoId { get; private set; }

    public Guid LancamentoId { get; private set; }

    public decimal Valor { get; private set; }

    protected QuitacaoSaldo()
    {
    }

    public static QuitacaoSaldo Criar(Guid negocioId, Guid fechamentoId, Guid lancamentoId, decimal valor) => new()
    {
        NegocioId = negocioId,
        FechamentoComissaoId = fechamentoId,
        LancamentoId = lancamentoId,
        Valor = valor,
    };
}

/// <summary>Um lançamento ainda em aberto (valor menos o já quitado), na ordem em que deve ser descontado.</summary>
public sealed record SaldoEmAberto(Guid LancamentoId, TipoLancamentoSaldo Tipo, decimal Aberto);

public sealed record DescontoAplicado(Guid LancamentoId, TipoLancamentoSaldo Tipo, decimal Valor);

/// <summary>
/// Quitação no fechamento (seção 7): os lançamentos em aberto, do mais antigo para o mais novo, são descontados da
/// comissão bruta até ela acabar. O líquido nunca fica negativo; o que não coube continua em aberto.
/// </summary>
public static class CalculadoraQuitacao
{
    public static (IReadOnlyList<DescontoAplicado> Descontos, decimal Liquido, decimal Restante) Aplicar(
        decimal comissaoBruta, IEnumerable<SaldoEmAberto> emAbertoDoMaisAntigo)
    {
        var disponivel = Math.Max(0m, comissaoBruta);
        var descontos = new List<DescontoAplicado>();
        var restante = 0m;

        foreach (var saldo in emAbertoDoMaisAntigo.Where(s => s.Aberto > 0))
        {
            var desconto = Math.Min(disponivel, saldo.Aberto);
            if (desconto > 0)
                descontos.Add(new DescontoAplicado(saldo.LancamentoId, saldo.Tipo, desconto));
            disponivel -= desconto;
            restante += saldo.Aberto - desconto;
        }

        return (descontos, disponivel, restante);
    }
}
