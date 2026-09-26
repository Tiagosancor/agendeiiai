namespace Plataforma.Aplicacao.Cadastros;

/// <summary>
/// Exclusão de usuário, profissional ou serviço (seção 7). Sem histórico, o registro é
/// apagado de fato; com histórico, é excluído logicamente (e, se for pessoa, perde os dados
/// pessoais). Algumas regras bloqueiam a exclusão (próprio usuário, último Administrador,
/// profissional com agendamentos futuros).
/// </summary>
public enum TipoResultadoExclusao
{
    NaoEncontrado,
    ApagadoDefinitivamente,
    ExcluidoLogicamente,
    Bloqueado,
}

public sealed record ResultadoExclusao(TipoResultadoExclusao Tipo, string? Motivo = null)
{
    public static ResultadoExclusao NaoEncontrado { get; } = new(TipoResultadoExclusao.NaoEncontrado);

    public static ResultadoExclusao Bloqueado(string motivo) => new(TipoResultadoExclusao.Bloqueado, motivo);

    public static ResultadoExclusao Concluido(bool temHistorico) =>
        new(temHistorico ? TipoResultadoExclusao.ExcluidoLogicamente : TipoResultadoExclusao.ApagadoDefinitivamente);
}

/// <summary>
/// O que a janela de confirmação explica antes de excluir: se some de vez ou fica no
/// histórico, quantos agendamentos futuros existem e, se houver, por que não dá para excluir
/// ainda. <see cref="Bloqueio"/> preenchido = a exclusão seria recusada agora.
/// </summary>
public sealed record PreviaExclusao(
    string Nome,
    bool TemHistorico,
    int AgendamentosFuturos,
    string? Bloqueio,
    IReadOnlyList<AgendamentoFuturoParaExclusao> Futuros);

/// <summary>Agendamento futuro de um profissional que vai ser excluído — transferir ou cancelar antes.</summary>
public sealed record AgendamentoFuturoParaExclusao(
    Guid Id,
    DateTimeOffset Inicio,
    DateTimeOffset Fim,
    string Cliente,
    IReadOnlyList<string> Servicos,
    IReadOnlyList<OpcaoTransferencia> ProfissionaisPossiveis);

/// <summary>Profissional ativo que executa todos os serviços do agendamento (a disponibilidade é checada ao transferir).</summary>
public sealed record OpcaoTransferencia(Guid Id, string Nome);

/// <summary>Regra de negócio que impede a operação (ex.: último Administrador) — vira HTTP 409.</summary>
public sealed class OperacaoCadastroBloqueadaException(string motivo) : Exception(motivo);
