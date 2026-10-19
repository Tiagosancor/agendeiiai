namespace Plataforma.Aplicacao.WhatsAppAgente;

public sealed record ContextoRespostaAgente(
    Guid NegocioId, Guid InstanciaId, Guid ConversaId, string TelefoneCliente, string TextoRecebido,
    DateTimeOffset? LimiteHistorico = null);
// LimiteHistorico: instante da última mensagem do lote que esta rodada vai marcar como processado. O histórico enviado à IA
// só inclui mensagens até ele (inclusive); o que chegar depois fica pendente para a rodada seguinte, sem ser respondido duas vezes.

/// <summary>
/// Ponto de troca entre o eco temporário da Fase 1 e o orquestrador de IA da Fase 2. Devolve o texto a
/// enviar ao cliente, ou <c>null</c> para não responder.
/// </summary>
public interface IRespostaAgente
{
    Task<string?> ResponderAsync(ContextoRespostaAgente contexto, CancellationToken cancellationToken = default);
}
