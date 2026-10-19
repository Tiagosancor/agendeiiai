namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>
/// Agente de WhatsApp (Fase 1 do plano) — seção "Whatsapp": <c>Whatsapp__WebhookSecret</c> (segredo no caminho do
/// webhook) e <c>Whatsapp__UrlPublicaApi</c> (URL da API que a Evolution alcança, usada no webhook por instância).
/// Independente da seção "WhatsApp:Evolution" do envio de códigos de confirmação.
/// </summary>
public sealed class OpcoesAgenteWhatsApp
{
    public const string Secao = "Whatsapp";

    /// <summary>Sem segredo (16+ caracteres) o webhook responde 404 e a instância não é criada com webhook.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Ex.: <c>https://api.exemplo.com.br</c> ou <c>http://api:8080</c> no compose.</summary>
    public string? UrlPublicaApi { get; set; }

    public bool WebhookConfigurado => !string.IsNullOrWhiteSpace(WebhookSecret) && WebhookSecret.Length >= 16;
}

/// <summary>Evolution API do agente: <c>Evolution__BaseUrl</c>, <c>Evolution__ApiKey</c> (chave global, só no backend).</summary>
public sealed class OpcoesEvolution
{
    public const string Secao = "Evolution";

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public int TimeoutSegundos { get; set; } = 10;
}

/// <summary>Redis: <c>Redis__ConnectionString</c>. Vazio = sem Redis (a deduplicação fica só com o índice do banco).</summary>
public sealed class OpcoesRedis
{
    public const string Secao = "Redis";

    public string? ConnectionString { get; set; }
}
