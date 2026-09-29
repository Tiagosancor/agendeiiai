namespace Plataforma.Aplicacao.Estoque;

/// <summary>
/// Produtos e movimentos de estoque (seção 7, "Estoque"). A quantidade só muda por movimento, sempre com a
/// linha do produto travada — duas saídas simultâneas nunca deixam o estoque negativo.
/// </summary>
public interface IGerenciadorEstoque
{
    Task<IReadOnlyList<ProdutoResumo>> ListarAsync(CancellationToken cancellationToken = default);

    /// <summary>A quantidade inicial entra como um movimento de entrada ("Estoque inicial"), nunca direto no número.</summary>
    Task<Guid> CriarAsync(CriarProduto dados, CancellationToken cancellationToken = default);

    Task<bool> AtualizarAsync(Guid produtoId, AtualizarProduto dados, CancellationToken cancellationToken = default);

    Task<bool> AlterarAtivoAsync(Guid produtoId, bool ativo, CancellationToken cancellationToken = default);

    /// <summary>Só sem venda nem consumo interno; com histórico, lança <c>OperacaoCadastroBloqueadaException</c> (desative).</summary>
    Task<bool> ExcluirAsync(Guid produtoId, CancellationToken cancellationToken = default);

    Task<ProdutoResumo?> RegistrarEntradaAsync(Guid produtoId, RegistrarEntrada dados, CancellationToken cancellationToken = default);

    Task<ProdutoResumo?> RegistrarAjusteAsync(Guid produtoId, RegistrarAjuste dados, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MovimentoEstoqueResumo>?> ListarMovimentosAsync(Guid produtoId, CancellationToken cancellationToken = default);

    /// <summary>As listas "Estoque baixo" e "Esgotado", recalculadas a cada consulta (sem valores de custo).</summary>
    Task<AlertasEstoque> ObterAlertasAsync(CancellationToken cancellationToken = default);
}

/// <summary><c>Situacao</c>: <c>Normal</c>, <c>EstoqueBaixo</c>, <c>Esgotado</c> ou <c>Inativo</c>.</summary>
public sealed record ProdutoResumo(
    Guid Id, string Nome, string? Categoria, decimal PrecoCusto, decimal PrecoVenda, int QuantidadeEstoque,
    int QuantidadeMinima, bool Ativo, string Situacao);

public sealed record CriarProduto(
    string Nome, string? Categoria, decimal PrecoCusto, decimal PrecoVenda, int QuantidadeInicial = 0, int? QuantidadeMinima = null);

public sealed record AtualizarProduto(string Nome, string? Categoria, decimal PrecoCusto, decimal PrecoVenda, int QuantidadeMinima);

public sealed record RegistrarEntrada(int Quantidade, decimal CustoUnitario, string? Fornecedor = null, string? Observacao = null);

/// <summary>Correção de contagem: a quantidade que está de fato na prateleira, e o porquê.</summary>
public sealed record RegistrarAjuste(int QuantidadeContada, string Motivo);

public sealed record MovimentoEstoqueResumo(
    Guid Id, string Tipo, int Quantidade, decimal? ValorUnitario, int QuantidadeAntes, int QuantidadeDepois,
    DateTimeOffset Data, string? Usuario, string? Observacao, string? Fornecedor);

public sealed record AlertasEstoque(IReadOnlyList<ProdutoEmAlerta> EstoqueBaixo, IReadOnlyList<ProdutoEmAlerta> Esgotados);

public sealed record ProdutoEmAlerta(Guid Id, string Nome, int QuantidadeEstoque, int QuantidadeMinima);
