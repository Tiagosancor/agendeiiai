namespace Plataforma.Aplicacao.WhatsAppAgente;

public enum ResultadoConversaWhatsApp
{
    Ok = 0,

    /// <summary>Não existe conversa com esse id no negócio do token (ou ela foi anonimizada).</summary>
    NaoEncontrada = 1,
}

/// <summary>Por que o agente chamou um atendente. Lista fechada: texto livre do modelo nunca vai a e-mail, log ou banco.</summary>
public enum MotivoChamadaAtendente
{
    PedidoDoCliente = 1,
    Reclamacao = 2,
    ForaDoEscopo = 3,
    Outro = 4,
}

/// <param name="Marcada">A conversa passou a Humano nesta chamada (falso = já estava em Humano ou não existe).</param>
/// <param name="AvisarAtendente">Passou nos limites de e-mail (1 por conversa a cada N horas, teto por negócio/dia): avisar o dono.</param>
public sealed record ResultadoMarcarHumano(bool Marcada, bool AvisarAtendente);

/// <summary>
/// Atendimento humano das conversas do WhatsApp (Fase 4). Sempre no negócio do contexto. Cada transição é um único
/// <c>UPDATE</c> condicional no banco — duas ações simultâneas nunca deixam a conversa num estado misto.
/// </summary>
public interface IGerenciadorConversasWhatsApp
{
    /// <summary>
    /// O usuário do token assume a conversa: o agente fica em silêncio, as mensagens do cliente continuam gravadas e as pendências de
    /// agendamento ainda abertas são encerradas (sem efetivar nada). Idempotente. Funciona com qualquer provedor de IA.
    /// </summary>
    Task<ResultadoConversaWhatsApp> AssumirAsync(Guid conversaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devolve ao agente sem responder o que se acumulou (o "processado até" vai para a última mensagem do cliente); o agente responde
    /// a próxima mensagem nova. Idempotente.
    /// </summary>
    Task<ResultadoConversaWhatsApp> DevolverAsync(Guid conversaId, CancellationToken cancellationToken = default);

    /// <summary>Usado pela ferramenta <c>chamar_atendente</c> (sem usuário). Só sai de Agente e só com o serviço de IA contratado (conferido no mesmo UPDATE); já em Humano devolve <c>Marcada = false</c>.</summary>
    /// <param name="epocaEsperada">Época do atendimento em que a rodada começou; se a conversa já foi assumida/devolvida desde então, não marca.</param>
    Task<ResultadoMarcarHumano> MarcarHumanoPeloAgenteAsync(
        Guid conversaId, MotivoChamadaAtendente motivo, int? epocaEsperada = null, CancellationToken cancellationToken = default);
}
