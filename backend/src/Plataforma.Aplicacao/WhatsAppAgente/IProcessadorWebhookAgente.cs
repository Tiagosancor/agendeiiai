namespace Plataforma.Aplicacao.WhatsAppAgente;

public enum ResultadoWebhookAgente
{
    NaoConfigurado,
    SegredoInvalido,
    CorpoInvalido,
    Ignorado,
    Repetido,
    Processado,
}

public interface IProcessadorWebhookAgente
{
    Task<ResultadoWebhookAgente> ProcessarAsync(string? segredo, string corpo, CancellationToken cancellationToken = default);
}
