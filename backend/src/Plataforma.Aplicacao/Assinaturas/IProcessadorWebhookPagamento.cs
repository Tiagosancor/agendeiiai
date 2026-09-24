namespace Plataforma.Aplicacao.Assinaturas;

public enum ResultadoWebhook
{
    Processado,

    /// <summary>Evento já recebido antes — nada feito, mas o provedor recebe 200 (senão reenviaria).</summary>
    Repetido,

    ProvedorDesconhecido,
    AssinaturaInvalida,
}

public interface IProcessadorWebhookPagamento
{
    Task<ResultadoWebhook> ProcessarAsync(string provedor, RequisicaoWebhook requisicao, CancellationToken cancellationToken = default);
}
