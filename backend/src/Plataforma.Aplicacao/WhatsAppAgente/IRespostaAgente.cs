namespace Plataforma.Aplicacao.WhatsAppAgente;

public sealed record ContextoRespostaAgente(
    Guid NegocioId, Guid InstanciaId, Guid ConversaId, string TelefoneCliente, string TextoRecebido);

/// <summary>
/// Ponto de troca entre o eco temporário da Fase 1 e o orquestrador de IA da Fase 2. Devolve o texto a
/// enviar ao cliente, ou <c>null</c> para não responder.
/// </summary>
public interface IRespostaAgente
{
    Task<string?> ResponderAsync(ContextoRespostaAgente contexto, CancellationToken cancellationToken = default);
}
