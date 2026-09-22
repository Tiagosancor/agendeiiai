namespace Plataforma.Aplicacao.Agendamentos;

/// <summary>
/// Cria e gerencia agendamentos. A garantia contra sobreposição é a exclusion constraint
/// do Postgres (seção 8.2.1) — aqui só orquestramos a transação: expira reservas vencidas
/// do profissional, valida expediente/almoço/bloqueios (o que a constraint não cobre) e
/// insere. Se a constraint ainda assim recusar (perdeu a corrida), devolve conflito com
/// sugestões — nunca deixa vazar exceção de banco (seção 8.2.4).
/// </summary>
public interface IServicoAgendamentos
{
    /// <summary>Cria já confirmado — fluxo do painel (Sprint 2).</summary>
    Task<ResultadoAgendamento> CriarAsync(CriarAgendamento dados, CancellationToken cancellationToken = default);

    /// <summary>Reserva temporária de 10 min (seção 8.2.2) — encaixe manual do painel, com cliente já conhecido.</summary>
    Task<ResultadoAgendamento> CriarReservaAsync(CriarAgendamento dados, CancellationToken cancellationToken = default);

    Task<ResultadoAgendamento> ConfirmarReservaAsync(Guid agendamentoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserva anônima do assistente público (seção 6.2.2/8.2.2): o horário é reservado ao
    /// ser escolhido, antes de telefone e código existirem — sem cliente vinculado ainda.
    /// </summary>
    Task<ResultadoAgendamento> CriarReservaPublicaAsync(CriarReservaPublica dados, CancellationToken cancellationToken = default);

    /// <summary>
    /// Identifica/cria o cliente pelo telefone (seção 8.1.4, mesma transação), aplica o
    /// cupom se houver e confirma a reserva — só chamado depois do código validado.
    /// </summary>
    Task<ResultadoAgendamento> ConfirmarReservaPublicaAsync(
        ConfirmarReservaPublica dados, CancellationToken cancellationToken = default);

    /// <summary>Só para feedback imediato do botão "Aplicar" (seção 6.2.4) — não grava nada; a revalidação que vale é a de <see cref="ConfirmarReservaPublicaAsync"/>.</summary>
    Task<ResultadoPreVisualizacaoCupom> PreVisualizarCupomAsync(
        Guid agendamentoId, string codigoCupom, CancellationToken cancellationToken = default);

    Task<bool> CancelarAsync(Guid agendamentoId, CancellationToken cancellationToken = default);

    Task<ResultadoAgendamento> MoverAsync(Guid agendamentoId, DateTimeOffset novoInicio, CancellationToken cancellationToken = default);

    Task<bool> MarcarConcluidoAsync(Guid agendamentoId, CancellationToken cancellationToken = default);

    Task<bool> MarcarFaltouAsync(Guid agendamentoId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgendamentoResumo>> ListarAgendaDoDiaAsync(
        Guid profissionalId, DateOnly data, CancellationToken cancellationToken = default);

    /// <summary>Resumo para a tela de sucesso e para os links de cancelar/remarcar (seção 6.3) — nunca inclui dado de outro cliente.</summary>
    Task<DetalhePublicoAgendamento?> ObterDetalhePublicoAsync(Guid agendamentoId, CancellationToken cancellationToken = default);
}

public sealed record DetalhePublicoAgendamento(
    Guid Id, string NomeNegocio, string Local, DateTimeOffset Inicio, DateTimeOffset Fim,
    IReadOnlyList<string> Servicos, decimal Total, string Status);

public sealed record CriarAgendamento(
    Guid ProfissionalId, Guid? ClienteId, IReadOnlyList<Guid> ServicoIds, DateTimeOffset Inicio, string? Observacoes = null);

public sealed record CriarReservaPublica(Guid ProfissionalId, IReadOnlyList<Guid> ServicoIds, DateTimeOffset Inicio);

public sealed record ConfirmarReservaPublica(
    Guid AgendamentoId, string NomeCliente, string TelefoneCliente, string? EmailCliente,
    string? Observacoes, string? CodigoCupom);

public sealed record ResultadoPreVisualizacaoCupom(bool Sucesso, decimal Desconto = 0m, string? MensagemErro = null);

public sealed record AgendamentoResumo(
    Guid Id, Guid ProfissionalId, Guid? ClienteId, string ClienteNome, DateTimeOffset Inicio, DateTimeOffset Fim,
    string Status, string? Observacoes, IReadOnlyList<string> Servicos, decimal Total);

public sealed record ResultadoAgendamento(
    bool Sucesso, Guid? AgendamentoId = null, string? MensagemErro = null,
    IReadOnlyList<DateTimeOffset>? ProximosHorariosLivres = null, bool Conflito = false)
{
    public static ResultadoAgendamento ComSucesso(Guid agendamentoId) => new(true, agendamentoId);

    /// <summary>Erro de validação (fora do expediente, bloqueado) — 400.</summary>
    public static ResultadoAgendamento ComErro(string mensagem) => new(false, MensagemErro: mensagem);

    /// <summary>Perdeu a corrida pro mesmo horário — a exclusion constraint recusou (seção 8.2.1/8.2.4) — 409.</summary>
    public static ResultadoAgendamento ComConflito(string mensagem, IReadOnlyList<DateTimeOffset> proximosHorarios) =>
        new(false, MensagemErro: mensagem, ProximosHorariosLivres: proximosHorarios, Conflito: true);
}
