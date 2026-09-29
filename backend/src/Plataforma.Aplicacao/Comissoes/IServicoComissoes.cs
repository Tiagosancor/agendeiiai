namespace Plataforma.Aplicacao.Comissoes;

/// <summary>
/// Comissões dos profissionais (seção 7). Os valores de cada atendimento são gravados na
/// conclusão (<c>AgendamentoServico.Comissao*</c>); aqui só se altera o percentual e se consulta.
/// Datas do filtro são dias inteiros no fuso do negócio, pela data do atendimento.
/// </summary>
public interface IServicoComissoes
{
    Task<ConfiguracaoComissao?> ObterConfiguracaoAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Altera o percentual (vale só para atendimentos concluídos daqui em diante) e o acerto por
    /// quinzena, registrando na auditoria o que mudou.
    /// </summary>
    Task<bool> DefinirConfiguracaoAsync(Guid profissionalId, ConfiguracaoComissao configuracao, CancellationToken cancellationToken = default);

    /// <summary>"Minhas comissões": o profissional vem sempre do usuário logado, nunca de parâmetro.</summary>
    Task<ComissoesDoProfissional> ListarMinhasAsync(FiltroComissoes filtro, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResumoComissaoProfissional>> ResumirPorProfissionalAsync(
        DateOnly de, DateOnly ate, CancellationToken cancellationToken = default);

    Task<ComissoesDoProfissional?> ListarDoProfissionalAsync(
        Guid profissionalId, FiltroComissoes filtro, CancellationToken cancellationToken = default);

    /// <summary>
    /// Comissão de produto de quem vende sem cadastro de profissional (ex.: Recepcionista): quem tem percentual ou
    /// vendeu no período.
    /// </summary>
    Task<IReadOnlyList<ResumoComissaoVendedor>> ResumirVendedoresSemProfissionalAsync(
        DateOnly de, DateOnly ate, CancellationToken cancellationToken = default);

    /// <summary>Nulo: usuário não encontrado. Usuário vinculado a profissional usa o percentual do profissional.</summary>
    Task<PercentualComissaoProdutoUsuario?> ObterPercentualProdutoDoUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Lança <see cref="ArgumentException"/> para usuário vinculado a profissional (o percentual fica na ficha dele).</summary>
    Task<bool> DefinirPercentualProdutoDoUsuarioAsync(Guid usuarioId, decimal percentual, CancellationToken cancellationToken = default);
}

/// <summary><c>ProfissionalId</c> preenchido: o percentual que vale é o do profissional vinculado, na ficha dele.</summary>
public sealed record PercentualComissaoProdutoUsuario(decimal Percentual, Guid? ProfissionalId);

public sealed record DefinirPercentualProduto(decimal Percentual);

public sealed record TotaisComissaoProduto(decimal TotalComissao, decimal TotalVendido, int QuantidadeVendas)
{
    public static TotaisComissaoProduto Zero { get; } = new(0m, 0m, 0);
}

/// <summary>Uma venda em que a pessoa foi o vendedor. <c>Cliente</c> é só o primeiro nome.</summary>
public sealed record ItemComissaoProduto(
    Guid VendaId, DateTimeOffset Data, string Produtos, string? Cliente, decimal TotalVendido, decimal Percentual, decimal Comissao);

/// <summary>Comissão sobre venda de produto (seção 7), sempre separada da de serviço. Lista as vendas mais recentes do período.</summary>
public sealed record ComissoesProduto(
    decimal? PercentualAtual, TotaisComissaoProduto Totais, IReadOnlyList<ItemComissaoProduto> Itens)
{
    public static ComissoesProduto Vazio { get; } = new(null, TotaisComissaoProduto.Zero, []);
}

public sealed record ResumoComissaoVendedor(Guid UsuarioId, string Nome, bool Ativo, decimal PercentualAtual, TotaisComissaoProduto Totais);

/// <summary><c>PercentualProdutoVenda</c> nulo no PUT: não muda (o front de antes do recurso não o envia).</summary>
public sealed record ConfiguracaoComissao(decimal Percentual, bool AcertoPorQuinzena, decimal? PercentualProdutoVenda = null);

public sealed record FiltroComissoes(DateOnly De, DateOnly Ate, int Pagina = 1, int TamanhoPagina = 20);

public sealed record TotaisComissao(decimal TotalComissao, decimal TotalAtendido, int QuantidadeServicos)
{
    public static TotaisComissao Zero { get; } = new(0m, 0m, 0);
}

/// <summary>Uma linha de serviço concluída. <c>Cliente</c> é só o primeiro nome.</summary>
public sealed record ItemComissao(
    Guid AgendamentoId, DateTimeOffset Inicio, string Servico, string Cliente, decimal ValorCobrado, decimal Percentual, decimal Comissao);

/// <summary>
/// <c>ProfissionalId</c> nulo: o usuário logado não está vinculado a nenhum profissional (sem
/// comissão de serviço) — a tela mostra zeros de serviço e, se ele vende, a comissão de produto.
/// <c>Produtos</c> é a comissão de produto, separada; <c>TotalGeral</c>, a soma das duas.
/// </summary>
public sealed record ComissoesDoProfissional(
    Guid? ProfissionalId, string? NomeProfissional, decimal? PercentualAtual, TotaisComissao Totais,
    IReadOnlyList<ItemComissao> Itens, int Pagina, int TamanhoPagina, int TotalItens,
    ComissoesProduto Produtos, decimal TotalGeral);

public sealed record ResumoComissaoProfissional(
    Guid ProfissionalId, string Nome, bool Ativo, decimal PercentualAtual, TotaisComissao Totais,
    decimal PercentualProdutoAtual, TotaisComissaoProduto Produtos);
