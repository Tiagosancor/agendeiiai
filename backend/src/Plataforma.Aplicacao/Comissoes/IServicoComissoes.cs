namespace Plataforma.Aplicacao.Comissoes;

/// <summary>
/// Comissões dos profissionais (seção 7). Os valores de cada atendimento são gravados na
/// conclusão (<c>AgendamentoServico.Comissao*</c>); aqui só se altera o percentual e se consulta.
/// Datas do filtro são dias inteiros no fuso do negócio, pela data do atendimento.
/// </summary>
public interface IServicoComissoes
{
    Task<decimal?> ObterPercentualAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    /// <summary>Altera o percentual (vale só para atendimentos concluídos daqui em diante) e registra na auditoria.</summary>
    Task<bool> DefinirPercentualAsync(Guid profissionalId, decimal percentual, CancellationToken cancellationToken = default);

    /// <summary>"Minhas comissões": o profissional vem sempre do usuário logado, nunca de parâmetro.</summary>
    Task<ComissoesDoProfissional> ListarMinhasAsync(FiltroComissoes filtro, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResumoComissaoProfissional>> ResumirPorProfissionalAsync(
        DateOnly de, DateOnly ate, CancellationToken cancellationToken = default);

    Task<ComissoesDoProfissional?> ListarDoProfissionalAsync(
        Guid profissionalId, FiltroComissoes filtro, CancellationToken cancellationToken = default);
}

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
/// comissão de serviço) — a tela mostra zeros e explica.
/// </summary>
public sealed record ComissoesDoProfissional(
    Guid? ProfissionalId, string? NomeProfissional, decimal? PercentualAtual, TotaisComissao Totais,
    IReadOnlyList<ItemComissao> Itens, int Pagina, int TamanhoPagina, int TotalItens);

public sealed record ResumoComissaoProfissional(
    Guid ProfissionalId, string Nome, bool Ativo, decimal PercentualAtual, TotaisComissao Totais);
