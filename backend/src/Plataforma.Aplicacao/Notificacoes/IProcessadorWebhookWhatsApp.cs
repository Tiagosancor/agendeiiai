namespace Plataforma.Aplicacao.Notificacoes;

/// <summary>
/// Callbacks de status de mensagem da instância de WhatsApp (<c>POST /webhooks/evolution/status</c>):
/// enviada, entregue ou falhou. Atualiza o canal no registro de origem (código de confirmação ou
/// aviso ao profissional). Só com o segredo compartilhado; idempotente como o webhook de
/// pagamento (seção 8.6).
/// </summary>
public interface IProcessadorWebhookWhatsApp
{
    Task<ResultadoWebhookWhatsApp> ProcessarAsync(string? token, string corpo, CancellationToken cancellationToken = default);
}

public enum ResultadoWebhookWhatsApp
{
    /// <summary>Provedor atual não é o Evolution API (ou sem segredo configurado) — a rota nem existe.</summary>
    NaoConfigurado,
    TokenInvalido,
    CorpoInvalido,
    Processado,
    /// <summary>Todos os eventos do corpo já tinham sido recebidos — nada mudou.</summary>
    Repetido,
    /// <summary>Evento que não é de status de mensagem (ex.: conexão, QR code) ou sem ID/status reconhecível.</summary>
    Ignorado,
}
