using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Estoque;

/// <summary>
/// Produto vendido ou usado no negócio (seção 7, "Estoque"). A quantidade em estoque <b>nunca</b> é
/// editada direto: só muda por um <see cref="MovimentoEstoque"/> (entrada, venda, consumo interno ou
/// ajuste), que grava a quantidade antes e depois — o histórico sempre reconstrói o número atual.
/// </summary>
public class Produto : EntidadeBase, IEntidadeDoNegocio
{
    /// <summary>Sugestão de quantidade mínima de alerta para produto novo (confirmada pelo dono: 3).</summary>
    public const int QuantidadeMinimaPadrao = 3;

    public Guid NegocioId { get; private set; }

    public string Nome { get; private set; } = string.Empty;

    public string? Categoria { get; private set; }

    /// <summary>O que o negócio paga ao comprar. Sensível: fica fora de notificações, e-mails e página pública.</summary>
    public decimal PrecoCusto { get; private set; }

    public decimal PrecoVenda { get; private set; }

    public int QuantidadeEstoque { get; private set; }

    /// <summary>Na ou abaixo dela, o produto entra em "Estoque baixo"; em zero, em "Esgotado".</summary>
    public int QuantidadeMinima { get; private set; }

    public bool Ativo { get; private set; } = true;

    public bool EstoqueBaixo => Ativo && QuantidadeEstoque > 0 && QuantidadeEstoque <= QuantidadeMinima;

    public bool Esgotado => Ativo && QuantidadeEstoque == 0;

    protected Produto()
    {
    }

    public static Produto Criar(
        Guid negocioId, string nome, string? categoria, decimal precoCusto, decimal precoVenda, int quantidadeMinima = QuantidadeMinimaPadrao)
    {
        var produto = new Produto { NegocioId = negocioId };
        produto.AtualizarDados(nome, categoria, precoCusto, precoVenda, quantidadeMinima);
        return produto;
    }

    public void AtualizarDados(string nome, string? categoria, decimal precoCusto, decimal precoVenda, int quantidadeMinima)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome do produto é obrigatório.", nameof(nome));
        if (precoCusto < 0 || precoVenda < 0)
            throw new ArgumentException("Preço não pode ser negativo.");
        if (quantidadeMinima < 0)
            throw new ArgumentException("A quantidade mínima não pode ser negativa.", nameof(quantidadeMinima));

        Nome = nome.Trim();
        Categoria = string.IsNullOrWhiteSpace(categoria) ? null : categoria.Trim();
        PrecoCusto = decimal.Round(precoCusto, 2);
        PrecoVenda = decimal.Round(precoVenda, 2);
        QuantidadeMinima = quantidadeMinima;
    }

    public void Ativar() => Ativo = true;

    public void Desativar() => Ativo = false;

    /// <summary>Reposição/compra: soma ao estoque, com o custo daquela compra (pode diferir do cadastrado).</summary>
    public MovimentoEstoque RegistrarEntrada(
        int quantidade, decimal custoUnitario, string? fornecedor, string? observacao, Guid? usuarioId, DateTimeOffset agora)
    {
        if (quantidade <= 0)
            throw new ArgumentException("A quantidade da entrada precisa ser maior que zero.", nameof(quantidade));
        if (custoUnitario < 0)
            throw new ArgumentException("O custo não pode ser negativo.", nameof(custoUnitario));

        return Movimentar(TipoMovimentoEstoque.Entrada, quantidade, decimal.Round(custoUnitario, 2), usuarioId, agora, observacao, fornecedor);
    }

    /// <summary>
    /// Venda ou consumo interno: desconta do estoque. Nunca deixa o estoque negativo — lança
    /// <see cref="EstoqueInsuficienteException"/> com a quantidade disponível.
    /// </summary>
    public MovimentoEstoque RegistrarSaida(
        TipoMovimentoEstoque tipo, int quantidade, decimal valorUnitario, string? observacao, Guid? usuarioId, DateTimeOffset agora)
    {
        if (tipo is not (TipoMovimentoEstoque.Venda or TipoMovimentoEstoque.ConsumoInterno))
            throw new ArgumentException("Saída de estoque é venda ou consumo interno.", nameof(tipo));
        if (quantidade <= 0)
            throw new ArgumentException("A quantidade precisa ser maior que zero.", nameof(quantidade));
        if (quantidade > QuantidadeEstoque)
            throw new EstoqueInsuficienteException(Nome, QuantidadeEstoque);

        return Movimentar(tipo, -quantidade, decimal.Round(valorUnitario, 2), usuarioId, agora, observacao, fornecedor: null);
    }

    /// <summary>Correção manual de contagem (perda, quebra, balanço físico): informa a quantidade contada, com motivo.</summary>
    public MovimentoEstoque RegistrarAjuste(int quantidadeContada, string motivo, Guid? usuarioId, DateTimeOffset agora)
    {
        if (quantidadeContada < 0)
            throw new ArgumentException("A quantidade contada não pode ser negativa.", nameof(quantidadeContada));
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("Informe o motivo do ajuste.", nameof(motivo));
        if (quantidadeContada == QuantidadeEstoque)
            throw new ArgumentException("A quantidade contada é igual à do estoque: não há o que ajustar.", nameof(quantidadeContada));

        return Movimentar(TipoMovimentoEstoque.Ajuste, quantidadeContada - QuantidadeEstoque, null, usuarioId, agora, motivo.Trim(), fornecedor: null);
    }

    private MovimentoEstoque Movimentar(
        TipoMovimentoEstoque tipo, int variacao, decimal? valorUnitario, Guid? usuarioId, DateTimeOffset agora, string? observacao, string? fornecedor)
    {
        var antes = QuantidadeEstoque;
        QuantidadeEstoque = antes + variacao;
        return MovimentoEstoque.Criar(NegocioId, Id, tipo, variacao, valorUnitario, antes, QuantidadeEstoque, usuarioId, agora, observacao, fornecedor);
    }
}
