namespace Plataforma.Aplicacao.Comissoes;

/// <summary>
/// Saldo devedor do profissional — ou do usuário que vende sem cadastro de profissional (seção 7): vales (adiantamento em dinheiro) e consumo interno de produto. Desconta
/// do valor a receber, nunca do faturamento, e é quitado no fechamento da quinzena. Dado de remuneração: nada daqui
/// vai para e-mail, notificação ou página pública.
/// </summary>
public interface IServicoSaldoDevedor
{
    /// <summary>Lança <see cref="ArgumentException"/> para dado inválido.</summary>
    Task<Guid> LancarValeAsync(LancarVale dados, CancellationToken cancellationToken = default);

    /// <summary>
    /// Baixa o estoque (linha do produto travada) e lança o consumo como saldo devedor. Nulo: produto não encontrado.
    /// Lança <c>EstoqueInsuficienteException</c> e <see cref="ArgumentException"/>.
    /// </summary>
    Task<Guid?> LancarConsumoAsync(Guid produtoId, LancarConsumo dados, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saldo em aberto de cada profissional (quem tem ou teve lançamento, e os ativos) e, depois, dos usuários sem cadastro de
    /// profissional que têm acerto por quinzena ou algum lançamento.
    /// </summary>
    Task<IReadOnlyList<SaldoDoProfissional>> ResumirAsync(CancellationToken cancellationToken = default);

    Task<SaldoDevedor?> DetalharAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    /// <summary>Nulo: usuário não encontrado ou vinculado a profissional (o saldo fica no profissional).</summary>
    Task<SaldoDevedor?> DetalharUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default);

    /// <summary>
    /// O do profissional vinculado ao usuário logado; sem vínculo, o do próprio usuário se ele tem acerto por quinzena ou
    /// algum lançamento (nulo nos demais casos). Só leitura.
    /// </summary>
    Task<SaldoDevedor?> DetalharMeuAsync(CancellationToken cancellationToken = default);

    /// <summary><paramref name="podeMexer"/> diz se quem pede pode mexer naquele tipo (vale ou consumo).</summary>
    Task<ResultadoLancamentoSaldo> AlterarAsync(
        Guid lancamentoId, AlterarLancamentoSaldo dados, Func<string, bool> podeMexer, CancellationToken cancellationToken = default);

    /// <summary>Consumo excluído devolve a quantidade ao estoque (movimento de entrada "Estorno de consumo interno").</summary>
    Task<ResultadoLancamentoSaldo> ExcluirAsync(
        Guid lancamentoId, bool confirmarQuitado, Func<string, bool> podeMexer, CancellationToken cancellationToken = default);
}

/// <summary>Exatamente um de <c>ProfissionalId</c> e <c>UsuarioId</c> (usuário que vende sem cadastro de profissional).</summary>
public sealed record LancarVale(Guid? ProfissionalId, decimal Valor, DateOnly? Data = null, string? Motivo = null, Guid? UsuarioId = null);

/// <summary>
/// <c>ValorUnitario</c> nulo: o preço de custo cadastrado (decisão do dono), editável na hora. Exatamente um de
/// <c>ProfissionalId</c> e <c>UsuarioId</c>.
/// </summary>
public sealed record LancarConsumo(
    Guid? ProfissionalId, int Quantidade, decimal? ValorUnitario = null, string? Observacao = null, Guid? UsuarioId = null);

/// <summary>
/// Vale: <c>Valor</c>, <c>Data</c> e <c>Descricao</c> (motivo). Consumo: <c>ValorUnitario</c> e <c>Descricao</c> (a quantidade é do
/// estoque). <c>ConfirmarQuitado</c>: o lançamento já foi descontado num fechamento — mexer muda aquele fechamento.
/// </summary>
public sealed record AlterarLancamentoSaldo(
    decimal? Valor = null, DateOnly? Data = null, decimal? ValorUnitario = null, string? Descricao = null, bool ConfirmarQuitado = false);

public enum ErroLancamentoSaldo
{
    NaoEncontrado,
    SemPermissao,
    QuitadoSemConfirmacao,
}

public sealed record ResultadoLancamentoSaldo(bool Sucesso, ErroLancamentoSaldo? Erro = null, string? Mensagem = null)
{
    public static ResultadoLancamentoSaldo Ok { get; } = new(true);

    public static ResultadoLancamentoSaldo Falha(ErroLancamentoSaldo erro, string mensagem) => new(false, erro, mensagem);
}

/// <summary><c>Aberto</c> = valor menos o que já foi descontado em fechamentos.</summary>
public sealed record LancamentoSaldoResumo(
    Guid Id, string Tipo, DateOnly Data, decimal Valor, decimal Aberto, string? Descricao, string? Produto, int? Quantidade,
    decimal? ValorUnitario, string? LancadoPor);

/// <summary><c>ProfissionalId</c> nulo e <c>UsuarioId</c> preenchido: usuário que vende sem cadastro de profissional.</summary>
public sealed record SaldoDevedor(
    Guid? ProfissionalId, string Nome, decimal ValesEmAberto, decimal ConsumoEmAberto, IReadOnlyList<LancamentoSaldoResumo> Lancamentos,
    Guid? UsuarioId = null)
{
    public decimal TotalEmAberto => ValesEmAberto + ConsumoEmAberto;
}

public sealed record SaldoDoProfissional(
    Guid? ProfissionalId, string Nome, bool Ativo, decimal ValesEmAberto, decimal ConsumoEmAberto, Guid? UsuarioId = null)
{
    public decimal TotalEmAberto => ValesEmAberto + ConsumoEmAberto;
}
