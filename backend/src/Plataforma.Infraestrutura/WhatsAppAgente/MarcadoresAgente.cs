namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Marcadores das mensagens <c>Sistema</c> que o servidor grava (<c>MensagemWhatsApp.NomeFerramenta</c>). Os de agendamento entram
/// no histórico da IA como turnos do modelo (sem token); o aviso de limite e o aviso automatizado não.
/// </summary>
public static class MarcadoresAgente
{
    public const string LimiteAviso = "limite_aviso";

    /// <summary>Mensagem fixa "atendimento automático indisponível" (negócio sem o serviço de IA contratado). Não vai ao histórico da IA.</summary>
    public const string IndisponivelAviso = "indisponivel_aviso";

    /// <summary>Texto fixo enviado quando o agente chama um atendente (Fase 4). Ainda não vai ao histórico do modelo.</summary>
    public const string Transferencia = "transferencia";

    public const string Resumo = "agendamento_resumo";
    public const string Confirmado = "agendamento_confirmado";
    public const string NaoConfirmado = "agendamento_nao_confirmado";
    public const string PrazoPassou = "agendamento_prazo_passou";

    /// <summary>
    /// Nota de contexto das ferramentas de consulta (<see cref="NotaContextoAgente"/>): vai ao histórico do modelo como turno em texto,
    /// fora da contagem de mensagens, e NUNCA ao cliente. Não está em <see cref="DoHistorico"/>: tem tratamento próprio.
    /// </summary>
    public const string ContextoConsulta = "contexto_consulta";

    public static readonly string[] DoHistorico = [Resumo, Confirmado, NaoConfirmado, PrazoPassou];
}
