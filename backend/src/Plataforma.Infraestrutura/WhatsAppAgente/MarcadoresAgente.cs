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

    /// <summary>Texto com o link de gestão que o servidor enviou (Fase 4). Gravado SEM o token ("[link de gestão enviado]") e conta no limite diário.</summary>
    public const string GestaoLink = "gestao_link";

    /// <summary>O link de gestão pedido não pôde ser enviado (o agendamento deixou de ser elegível). Não conta no limite de links.</summary>
    public const string GestaoNaoEnviada = "gestao_nao_enviada";

    /// <summary>Resumo do cancelamento que o servidor enviou pedindo o "SIM" (Fase 4, Fatia 7). Vai ao histórico do modelo.</summary>
    public const string GestaoResumo = "gestao_resumo";

    /// <summary>Texto do servidor confirmando que o cancelamento foi efetivado. A existência dele é o que distingue "anunciado" de "só gravado".</summary>
    public const string GestaoEfetivada = "gestao_efetivada";

    /// <summary>Texto do servidor dizendo que o cancelamento NÃO foi feito (o cliente disse não, o prazo passou ou a regra recusou).</summary>
    public const string GestaoNaoEfetivada = "gestao_nao_efetivada";

    /// <summary>Aviso único de que o prazo para confirmar o cancelamento terminou.</summary>
    public const string GestaoPrazoPassou = "gestao_prazo_passou";

    /// <summary>
    /// Nota de contexto das ferramentas de consulta (<see cref="NotaContextoAgente"/>): vai ao histórico do modelo como turno em texto,
    /// fora da contagem de mensagens, e NUNCA ao cliente. Não está em <see cref="DoHistorico"/>: tem tratamento próprio.
    /// </summary>
    public const string ContextoConsulta = "contexto_consulta";

    public static readonly string[] DoHistorico = [Resumo, Confirmado, NaoConfirmado, PrazoPassou, GestaoLink, GestaoNaoEnviada, GestaoResumo, GestaoEfetivada, GestaoNaoEfetivada, GestaoPrazoPassou];
}
