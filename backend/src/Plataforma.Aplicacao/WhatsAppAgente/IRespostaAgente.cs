namespace Plataforma.Aplicacao.WhatsAppAgente;

public sealed record ContextoRespostaAgente(
    Guid NegocioId, Guid InstanciaId, Guid ConversaId, string TelefoneCliente, string TextoRecebido,
    DateTimeOffset? LimiteHistorico = null);
// LimiteHistorico: instante da última mensagem do lote que esta rodada vai marcar como processado. O histórico enviado à IA
// só inclui mensagens até ele (inclusive); o que chegar depois fica pendente para a rodada seguinte, sem ser respondido duas vezes.

/// <summary>
/// O que o agente decidiu para a rodada. <c>Texto</c> nulo = não responder. <c>PendenciaPreparadaId</c> preenchido = a ferramenta
/// de preparar agendamento reservou o horário nesta rodada: o texto do modelo (se houver) é DESCARTADO e quem envia o resumo é o
/// servidor. Só o processador lê esse id; ele nunca vai ao modelo.
/// </summary>
public sealed record RespostaAgente(string? Texto, Guid? PendenciaPreparadaId = null);

/// <summary>
/// Ponto de troca entre o eco temporário da Fase 1 e o orquestrador de IA da Fase 2. Devolve a resposta a
/// enviar ao cliente (texto <c>null</c> para não responder).
/// </summary>
public interface IRespostaAgente
{
    Task<RespostaAgente> ResponderAsync(ContextoRespostaAgente contexto, CancellationToken cancellationToken = default);
}
