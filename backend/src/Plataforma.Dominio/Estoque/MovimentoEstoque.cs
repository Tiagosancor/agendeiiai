using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Estoque;

public enum TipoMovimentoEstoque
{
    /// <summary>Reposição/compra — soma.</summary>
    Entrada = 1,

    /// <summary>Venda a cliente — desconta e soma ao faturamento.</summary>
    Venda = 2,

    /// <summary>Uso pelo profissional — desconta e vira saldo devedor dele, fora do faturamento.</summary>
    ConsumoInterno = 3,

    /// <summary>Correção manual de contagem (perda, quebra, balanço), com motivo.</summary>
    Ajuste = 4,
}

/// <summary>
/// Toda mudança de quantidade de um <see cref="Produto"/> (seção 7). Nunca é alterado nem apagado:
/// grava a variação (positiva ou negativa) e a quantidade antes e depois, para auditoria e para não
/// depender de recalcular o histórico inteiro.
/// </summary>
public class MovimentoEstoque : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid ProdutoId { get; private set; }

    public TipoMovimentoEstoque Tipo { get; private set; }

    /// <summary>Variação no estoque: positiva na entrada, negativa na venda e no consumo; no ajuste, a diferença contada.</summary>
    public int Quantidade { get; private set; }

    /// <summary>Custo da compra (entrada) ou valor cobrado (venda); nulo no ajuste.</summary>
    public decimal? ValorUnitario { get; private set; }

    public int QuantidadeAntes { get; private set; }

    public int QuantidadeDepois { get; private set; }

    public DateTimeOffset Data { get; private set; }

    public Guid? UsuarioId { get; private set; }

    /// <summary>Observação livre; no ajuste, o motivo (obrigatório).</summary>
    public string? Observacao { get; private set; }

    public string? Fornecedor { get; private set; }

    protected MovimentoEstoque()
    {
    }

    internal static MovimentoEstoque Criar(
        Guid negocioId, Guid produtoId, TipoMovimentoEstoque tipo, int quantidade, decimal? valorUnitario,
        int quantidadeAntes, int quantidadeDepois, Guid? usuarioId, DateTimeOffset data, string? observacao, string? fornecedor) => new()
    {
        NegocioId = negocioId,
        ProdutoId = produtoId,
        Tipo = tipo,
        Quantidade = quantidade,
        ValorUnitario = valorUnitario,
        QuantidadeAntes = quantidadeAntes,
        QuantidadeDepois = quantidadeDepois,
        UsuarioId = usuarioId,
        Data = data,
        Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim(),
        Fornecedor = string.IsNullOrWhiteSpace(fornecedor) ? null : fornecedor.Trim(),
    };
}

/// <summary>Venda ou consumo maior que o estoque disponível (seção 7) — a mensagem já traz a quantidade real.</summary>
public sealed class EstoqueInsuficienteException(string produto, int disponivel)
    : InvalidOperationException($"Estoque insuficiente de {produto}: há {disponivel} disponível(is).")
{
    public int Disponivel { get; } = disponivel;
}
