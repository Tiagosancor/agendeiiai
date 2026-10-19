using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.WhatsAppAgente;

public enum PapelMensagemWhatsApp
{
    Usuario = 1,
    Assistente = 2,
    Ferramenta = 3,
    Sistema = 4,
}

/// <summary>
/// Mensagem de uma conversa. <see cref="IdExterno"/> (ID da mensagem na Evolution) é único por instância
/// — a segunda barreira de deduplicação depois do Redis.
/// </summary>
public class MensagemWhatsApp : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid InstanciaId { get; private set; }

    public Guid ConversaId { get; private set; }

    public string? IdExterno { get; private set; }

    public PapelMensagemWhatsApp Papel { get; private set; }

    public string Conteudo { get; private set; } = string.Empty;

    public string? NomeFerramenta { get; private set; }

    protected MensagemWhatsApp()
    {
    }

    /// <summary>Preenche o ID da Evolution depois que o envio (feito com a mensagem já reservada no banco) foi confirmado.</summary>
    public void DefinirIdExterno(string? idExterno) =>
        IdExterno = string.IsNullOrWhiteSpace(idExterno) ? null : idExterno.Trim();

    public MensagemWhatsApp(
        Guid negocioId, Guid instanciaId, Guid conversaId, string? idExterno,
        PapelMensagemWhatsApp papel, string conteudo, string? nomeFerramenta = null)
    {
        NegocioId = negocioId;
        InstanciaId = instanciaId;
        ConversaId = conversaId;
        IdExterno = string.IsNullOrWhiteSpace(idExterno) ? null : idExterno.Trim();
        Papel = papel;
        Conteudo = conteudo;
        NomeFerramenta = nomeFerramenta;
    }
}
