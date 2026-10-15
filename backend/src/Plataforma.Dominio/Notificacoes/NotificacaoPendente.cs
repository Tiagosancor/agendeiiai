using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Notificacoes;

/// <summary>Tipos de mensagem que passam pela outbox (B7.4-B). Códigos, senhas e convites seguem direto: o usuário pede outro.</summary>
public enum TipoNotificacao
{
    Confirmacao = 1,
    Lembrete60 = 2,
    Cancelamento = 3,
    ProfissionalNovo = 4,
    ProfissionalRemarcado = 5,
    ProfissionalCancelado = 6,
    LembreteRetorno = 7,
}

/// <summary>Cada canal é um item próprio: falha, retry e deduplicação de um nunca mexem no outro.</summary>
public enum CanalNotificacao
{
    Email = 1,
    WhatsApp = 2,
}

public enum StatusNotificacao
{
    Pendente = 1,

    /// <summary>Reivindicada por um despachante (lease). Só este estado e <see cref="Pendente"/> contam como "ativos".</summary>
    Enviando = 2,

    /// <summary>O provedor aceitou a mensagem (e-mail aceito pela API; WhatsApp aceito pela instância).</summary>
    Enviada = 3,

    /// <summary>Falha definitiva: recusa do provedor, tentativas esgotadas ou prazo de 24 h vencido.</summary>
    Falhou = 4,

    /// <summary>Descartada sem envio: obsoleta, sem destinatário, canal desligado, opt-out ou fora do prazo do lembrete.</summary>
    Cancelada = 5,

    /// <summary>
    /// Pode ter sido aceita pelo WhatsApp, mas não há como saber (timeout, resposta perdida, queda depois do início do envio).
    /// Nunca é reenviada sozinha — melhor faltar uma mensagem que duplicar; fica para acompanhamento administrativo.
    /// </summary>
    Desconhecida = 6,
}

/// <summary>Família de regras de tentativa e de rollout de um tipo.</summary>
public enum FamiliaNotificacao
{
    Lembrete60 = 1,
    Operacional = 2,
    Retorno = 3,
}

public static class TiposDeNotificacao
{
    public static FamiliaNotificacao Familia(this TipoNotificacao tipo) => tipo switch
    {
        TipoNotificacao.Lembrete60 => FamiliaNotificacao.Lembrete60,
        TipoNotificacao.LembreteRetorno => FamiliaNotificacao.Retorno,
        _ => FamiliaNotificacao.Operacional,
    };

    /// <summary>Versão determinística do horário (minutos desde 1970, UTC): distingue o horário antigo do novo sem timestamp instável.</summary>
    public static long VersaoDoHorario(DateTimeOffset inicio) => inicio.ToUniversalTime().UtcTicks / TimeSpan.TicksPerMinute;

    public static string ChaveConfirmacao(Guid agendamentoId) => $"agendamento:{agendamentoId}:confirmacao";

    public static string ChaveLembrete60(Guid agendamentoId, long versao) => $"agendamento:{agendamentoId}:lembrete60:{versao}";

    public static string ChaveCancelamento(Guid agendamentoId) => $"agendamento:{agendamentoId}:cancelamento";

    public static string ChaveProfissionalNovo(Guid agendamentoId, Guid profissionalId) =>
        $"agendamento:{agendamentoId}:profissional:novo:{profissionalId}";

    public static string ChaveProfissionalRemarcado(Guid agendamentoId, long versao) =>
        $"agendamento:{agendamentoId}:profissional:remarcado:{versao}";

    public static string ChaveProfissionalCancelado(Guid agendamentoId) => $"agendamento:{agendamentoId}:profissional:cancelado";

    public static string ChaveRetorno(Guid clienteId, Guid agendamentoBaseId) => $"cliente:{clienteId}:retorno:{agendamentoBaseId}";
}

/// <summary>
/// Mensagem a enviar, gravada na MESMA transação do efeito de negócio (outbox transacional). Guarda só identificadores e metadados
/// técnicos: nome, telefone, e-mail, observação e texto da mensagem nunca entram aqui — o destinatário e o conteúdo são montados na hora
/// do despacho, com o tenant e o estado atuais. Criada por <c>INSERT … ON CONFLICT DO NOTHING</c> (<c>OutboxNotificacoes</c>); depois disso
/// só o despachante a muda, sempre com a reivindicação (<see cref="ReivindicacaoId"/>) como fence.
/// </summary>
public class NotificacaoPendente : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public TipoNotificacao Tipo { get; private set; }

    public CanalNotificacao Canal { get; private set; }

    /// <summary>Única por (negócio, chave, canal): a repetição de um comando não cria outra mensagem.</summary>
    public string ChaveDeduplicacao { get; private set; } = string.Empty;

    public StatusNotificacao Status { get; private set; }

    /// <summary>Quantas vezes foi reivindicada com a chamada ao provedor realmente tentada (ou potencialmente tentada).</summary>
    public int Tentativas { get; private set; }

    public DateTimeOffset ProximaTentativaEm { get; private set; }

    /// <summary>Prazo final: depois dele não há nova tentativa (lembrete: antes do atendimento; operacional: 24 h; retorno: validade do dia).</summary>
    public DateTimeOffset ExpiraEm { get; private set; }

    /// <summary>Até quando a reivindicação atual vale. Vencida, outra instância recupera o item.</summary>
    public DateTimeOffset? LeaseAte { get; private set; }

    /// <summary>Token da reivindicação atual: toda gravação do despachante confere este valor (fence contra duas instâncias).</summary>
    public Guid? ReivindicacaoId { get; private set; }

    /// <summary>
    /// Instante em que o despachante gravou "vou chamar o provedor agora" (ponto sem retorno). Nulo com lease vencido = a
    /// queda foi antes da chamada, seguro voltar a <see cref="StatusNotificacao.Pendente"/>; preenchido = pode ter sido aceita.
    /// </summary>
    public DateTimeOffset? EnvioIniciadoEm { get; private set; }

    public DateTimeOffset? EnviadaEm { get; private set; }

    public DateTimeOffset? CanceladaEm { get; private set; }

    /// <summary>Código técnico estável (<see cref="CodigosOutbox"/>) — nunca resposta do provedor, stack trace ou dado pessoal.</summary>
    public string? UltimoErroCodigo { get; private set; }

    /// <summary>ID da mensagem no provedor de WhatsApp (o webhook de status o usa); nulo para e-mail.</summary>
    public string? IdMensagemProvedor { get; private set; }

    // Referências do "payload mínimo" (só identificadores).
    public Guid? AgendamentoId { get; private set; }

    public Guid? ClienteId { get; private set; }

    public Guid? ProfissionalId { get; private set; }

    /// <summary>Versão do horário do agendamento quando a mensagem foi criada (<see cref="TiposDeNotificacao.VersaoDoHorario"/>).</summary>
    public long? VersaoHorario { get; private set; }

    protected NotificacaoPendente()
    {
    }
}

/// <summary>Códigos estáveis de <see cref="NotificacaoPendente.UltimoErroCodigo"/> (cancelamento, falha ou ambiguidade).</summary>
public static class CodigosOutbox
{
    public const string FalhaProvedor = "falha_provedor";
    public const string RecusadaProvedor = "recusada_provedor";
    public const string ErroInterno = "erro_interno";
    public const string TentativasEsgotadas = "tentativas_esgotadas";
    public const string PrazoEsgotado = "prazo_esgotado";
    public const string Expirada = "expirada";
    public const string TimeoutAmbiguo = "resultado_ambiguo";
    public const string LeaseExpiradoAposInicio = "lease_expirado_apos_inicio";
    public const string AgendamentoObsoleto = "agendamento_obsoleto";
    public const string HorarioAlterado = "horario_alterado";
    public const string SemDestinatario = "sem_destinatario";
    public const string CanalDesativado = "canal_desativado";
    public const string OptOut = "opt_out";
    public const string ClienteInelegivel = "cliente_inelegivel";
    public const string NegocioInativo = "negocio_inativo";
    public const string ReferenciaInexistente = "referencia_inexistente";
}
