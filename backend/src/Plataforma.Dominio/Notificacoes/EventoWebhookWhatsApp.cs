using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Notificacoes;

/// <summary>
/// Status de mensagem já recebido do webhook do WhatsApp. O índice único em
/// <see cref="IdEvento"/> ("{mensagem}:{status}") faz a instância poder reenviar o mesmo evento
/// sem efeito duplicado — mesmo padrão de <c>EventoWebhookPagamento</c> (seção 8.6.6). Também
/// guarda o status de uma mensagem cujo registro ainda não tinha o ID gravado (o webhook pode
/// chegar antes de a API terminar de salvar o envio).
/// </summary>
public class EventoWebhookWhatsApp : EntidadeBase
{
    public string Provedor { get; private set; } = string.Empty;

    public string IdEvento { get; private set; } = string.Empty;

    public string IdMensagem { get; private set; } = string.Empty;

    public StatusCanal Status { get; private set; }

    protected EventoWebhookWhatsApp()
    {
    }

    public EventoWebhookWhatsApp(string provedor, string idMensagem, StatusCanal status)
    {
        if (string.IsNullOrWhiteSpace(idMensagem))
            throw new ArgumentException("O ID da mensagem é obrigatório.", nameof(idMensagem));

        Provedor = provedor;
        IdMensagem = idMensagem.Trim();
        Status = status;
        IdEvento = $"{IdMensagem}:{status}";
    }
}
