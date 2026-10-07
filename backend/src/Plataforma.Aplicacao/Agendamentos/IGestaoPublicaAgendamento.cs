namespace Plataforma.Aplicacao.Agendamentos;

/// <summary>Códigos estáveis (machine-readable) dos motivos pelos quais uma ação do link público não é permitida.</summary>
public static class MotivosGestaoPublica
{
    public const string StatusNaoPermite = "status_nao_permite";
    public const string AgendamentoPassado = "agendamento_passado";
    public const string AntecedenciaMinima = "antecedencia_minima";
    public const string NaoPermitido = "nao_permitido";
    public const string HorarioIndisponivel = "horario_indisponivel";
}

/// <summary>Decisão do servidor sobre uma ação; <c>CodigoMotivo</c>/<c>Motivo</c> só vêm quando não é permitida.</summary>
public sealed record AcaoGestaoPublica(bool Permitido, string? CodigoMotivo = null, string? Motivo = null)
{
    public static AcaoGestaoPublica Liberada { get; } = new(true);

    public static AcaoGestaoPublica Bloqueada(string codigo, string motivo) => new(false, codigo, motivo);
}

public sealed record AcoesGestaoPublica(AcaoGestaoPublica Cancelar, AcaoGestaoPublica Remarcar);

public sealed record ProfissionalGestaoPublica(Guid Id, string Nome);

public sealed record ServicoGestaoPublica(Guid ServicoId, string Nome, int DuracaoMinutos, decimal Preco);

/// <summary>
/// <c>LimiteParaAlterarEm</c>: último instante (UTC) em que cancelar/remarcar ainda é aceito pela regra de
/// antecedência (início menos a antecedência mínima).
/// </summary>
public sealed record RegrasGestaoPublica(int AntecedenciaMinimaHoras, DateTimeOffset LimiteParaAlterarEm);

/// <summary>
/// Representação canônica do agendamento no link público. Os campos de <see cref="DetalhePublicoAgendamento"/>
/// continuam com o mesmo nome e tipo (retrocompatível); o resto é aditivo. <c>Inicio</c>/<c>Fim</c> são instantes
/// (UTC); <c>Fuso</c> é o id IANA do estabelecimento, que dá o relógio de parede para exibir e para <c>data</c>.
/// </summary>
public sealed record DetalheGestaoPublica(
    Guid Id, string NomeNegocio, string Local, DateTimeOffset Inicio, DateTimeOffset Fim,
    IReadOnlyList<string> Servicos, decimal Total, string Status,
    string Fuso, int DuracaoMinutos, ProfissionalGestaoPublica Profissional,
    IReadOnlyList<ServicoGestaoPublica> ServicosDetalhe, RegrasGestaoPublica Regras, AcoesGestaoPublica Acoes);

/// <summary>
/// Horários livres para remarcar ESTE agendamento (profissional, serviços e duração vêm dele). Cada horário é um
/// instante UTC alinhado à grade de 15 min do relógio local de <c>Fuso</c>; <c>Data</c> é o dia local consultado.
/// </summary>
public sealed record HorariosRemarcacao(
    string Fuso, DateOnly Data, int DuracaoMinutos, Guid ProfissionalId, IReadOnlyList<DateTimeOffset> Horarios);

/// <summary>Ou há horários, ou a remarcação não é permitida (<c>Bloqueio</c> traz a decisão).</summary>
public sealed record ResultadoHorariosRemarcacao(HorariosRemarcacao? Horarios, AcaoGestaoPublica? Bloqueio);

public interface IGestaoPublicaAgendamento
{
    Task<DetalheGestaoPublica?> ObterAsync(Guid agendamentoId, CancellationToken cancellationToken = default);

    /// <summary>Nulo se o agendamento não existe.</summary>
    Task<ResultadoHorariosRemarcacao?> ListarHorariosParaRemarcacaoAsync(
        Guid agendamentoId, DateOnly data, CancellationToken cancellationToken = default);
}
