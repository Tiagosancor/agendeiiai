namespace Plataforma.Aplicacao.Comissoes;

/// <summary>
/// Fechamento de comissões por quinzena (seção 7). Os períodos são do negócio, com datas
/// escolhidas pelo Administrador; só profissionais com "acerto por quinzena" entram no fechamento.
/// </summary>
public interface IServicoQuinzenas
{
    Task<IReadOnlyList<QuinzenaResumo>> ListarAsync(CancellationToken cancellationToken = default);

    /// <summary>A seguinte à última (1–15, 16–fim do mês); editável na tela.</summary>
    Task<SugestaoQuinzena> SugerirProximaAsync(CancellationToken cancellationToken = default);

    Task<ResultadoQuinzena> CriarAsync(DateOnly inicio, DateOnly fim, CancellationToken cancellationToken = default);

    Task<ResultadoQuinzena> AlterarAsync(Guid periodoId, DateOnly inicio, DateOnly fim, CancellationToken cancellationToken = default);

    Task<ResultadoQuinzena> ExcluirAsync(Guid periodoId, CancellationToken cancellationToken = default);

    Task<DetalheQuinzena?> DetalharAsync(Guid periodoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fecha e grava um <c>FechamentoComissao</c> por profissional com acerto por quinzena. Com
    /// atendimentos ainda sem conclusão no período, só fecha com <paramref name="confirmarPendentes"/>.
    /// </summary>
    Task<ResultadoQuinzena> FecharAsync(Guid periodoId, bool confirmarPendentes, CancellationToken cancellationToken = default);

    /// <summary>Apaga os fechamentos e volta a quinzena para aberta. Motivo obrigatório, fica na auditoria.</summary>
    Task<ResultadoQuinzena> ReabrirAsync(Guid periodoId, string? motivo, CancellationToken cancellationToken = default);

    /// <summary>Visão do profissional logado: as quinzenas abertas (parcial) e as fechadas (valor final) — só os valores dele.</summary>
    Task<QuinzenasDoProfissional> ListarMinhasAsync(CancellationToken cancellationToken = default);
}

public enum ErroQuinzena
{
    NaoEncontrada,
    DatasInvalidas,
    Sobreposicao,
    JaFechada,
    NaoFechada,
    PendentesSemConfirmacao,
    MotivoObrigatorio,
}

public sealed record ResultadoQuinzena(
    bool Sucesso, Guid? PeriodoId = null, ErroQuinzena? Erro = null, string? Mensagem = null, string? Aviso = null,
    IReadOnlyList<AtendimentoPendente>? Pendentes = null)
{
    public static ResultadoQuinzena Ok(Guid periodoId, string? aviso = null) => new(true, periodoId, Aviso: aviso);

    public static ResultadoQuinzena Falha(ErroQuinzena erro, string mensagem, IReadOnlyList<AtendimentoPendente>? pendentes = null) =>
        new(false, Erro: erro, Mensagem: mensagem, Pendentes: pendentes);
}

public sealed record SugestaoQuinzena(DateOnly Inicio, DateOnly Fim);

/// <summary><c>DiasSemPeriodoAntes</c>: buraco entre o fim da quinzena anterior e o início desta (0 = emendadas ou é a primeira).</summary>
public sealed record QuinzenaResumo(
    Guid Id, DateOnly Inicio, DateOnly Fim, string Estado, DateTimeOffset? FechadoEm, string? FechadoPor, int DiasSemPeriodoAntes);

/// <summary>
/// Uma linha por profissional com acerto por quinzena. <c>Totais</c> é a comissão de serviço; <c>ComissaoProdutos</c>, a de
/// produto; <c>Vales</c>/<c>Consumo</c>, o saldo devedor descontado (do mais antigo ao mais novo, até a comissão acabar);
/// <c>Liquido</c>, o que ele recebe (nunca negativo); <c>SaldoRestante</c>, o que fica para a próxima quinzena. Na quinzena
/// aberta tudo é parcial.
/// </summary>
public sealed record LinhaQuinzena(
    Guid ProfissionalId, string Nome, TotaisComissao Totais, decimal ComissaoProdutos = 0m, decimal Vales = 0m, decimal Consumo = 0m,
    decimal Liquido = 0m, decimal SaldoRestante = 0m);

public sealed record AtendimentoPendente(Guid AgendamentoId, DateTimeOffset Inicio, string Profissional, string Status);

public sealed record DetalheQuinzena(QuinzenaResumo Quinzena, bool Parcial, IReadOnlyList<LinhaQuinzena> Linhas, IReadOnlyList<AtendimentoPendente> Pendentes);

public sealed record QuinzenaDoProfissional(
    Guid PeriodoId, DateOnly Inicio, DateOnly Fim, string Estado, bool Parcial, TotaisComissao Totais, decimal ComissaoProdutos = 0m,
    decimal Vales = 0m, decimal Consumo = 0m, decimal Liquido = 0m, decimal SaldoRestante = 0m);

/// <summary><c>AcertoPorQuinzena</c> falso: o profissional acompanha pelo filtro livre de datas, sem fechamento.</summary>
public sealed record QuinzenasDoProfissional(bool AcertoPorQuinzena, IReadOnlyList<QuinzenaDoProfissional> Quinzenas);
