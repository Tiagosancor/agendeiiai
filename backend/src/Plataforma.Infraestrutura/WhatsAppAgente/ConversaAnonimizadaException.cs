namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Gravar mensagem numa conversa que acabou de ser anonimizada (LGPD, G11). Lançada por <c>PlataformaDbContext.SaveChanges*</c> sob a trava
/// da conversa: a gravação inteira é desfeita. O webhook refaz a resolução (nova conversa); a rodada do agente é abandonada.
/// </summary>
public sealed class ConversaAnonimizadaException(Guid conversaId)
    : Exception("A conversa foi anonimizada; a mensagem não foi gravada.")
{
    public Guid ConversaId { get; } = conversaId;
}
