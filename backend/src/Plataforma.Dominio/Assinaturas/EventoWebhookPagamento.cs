using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Assinaturas;

/// <summary>Evento de webhook já recebido — o índice único (provedor, id do evento) garante que um evento repetido nunca é processado duas vezes (seção 8.6.6).</summary>
public class EventoWebhookPagamento : EntidadeBase
{
    public string Provedor { get; private set; } = string.Empty;

    public string IdEvento { get; private set; } = string.Empty;

    public string Tipo { get; private set; } = string.Empty;

    protected EventoWebhookPagamento()
    {
    }

    public EventoWebhookPagamento(string provedor, string idEvento, string tipo)
    {
        if (string.IsNullOrWhiteSpace(provedor) || string.IsNullOrWhiteSpace(idEvento))
            throw new ArgumentException("Provedor e ID do evento são obrigatórios.");

        Provedor = provedor.Trim().ToLowerInvariant();
        IdEvento = idEvento.Trim();
        Tipo = tipo;
    }
}
