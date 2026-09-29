using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Assinaturas;

/// <summary>Evento de webhook já recebido — o índice único (provedor, id do evento) garante que um evento repetido nunca é processado duas vezes (seção 8.6.6).</summary>
public class EventoWebhookPagamento : EntidadeBase
{
    public string Provedor { get; private set; } = string.Empty;

    public string IdEvento { get; private set; } = string.Empty;

    public string Tipo { get; private set; } = string.Empty;

    /// <summary>Corpo bruto recebido, para auditoria (nunca vai para log).</summary>
    public string? Corpo { get; private set; }

    /// <summary>O que foi feito: <c>Aplicado</c>, <c>Ignorado</c>, <c>AssinaturaDesconhecida</c>, <c>JaRegistrado</c>...</summary>
    public string? Resultado { get; private set; }

    protected EventoWebhookPagamento()
    {
    }

    public EventoWebhookPagamento(string provedor, string idEvento, string tipo, string? corpo = null)
    {
        if (string.IsNullOrWhiteSpace(provedor) || string.IsNullOrWhiteSpace(idEvento))
            throw new ArgumentException("Provedor e ID do evento são obrigatórios.");

        Provedor = provedor.Trim().ToLowerInvariant();
        IdEvento = idEvento.Trim();
        Tipo = tipo;
        Corpo = corpo;
    }

    public void RegistrarResultado(string resultado) => Resultado = resultado;
}
