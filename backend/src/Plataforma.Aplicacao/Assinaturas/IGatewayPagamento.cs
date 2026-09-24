namespace Plataforma.Aplicacao.Assinaturas;

/// <summary>
/// Contrato do gateway de cobrança da assinatura (seção 7). Hoje só existe a implementação
/// <c>Manual</c>; um gateway real (a escolher) entra como uma nova implementação desta
/// interface, sem mexer em quem a consome.
/// </summary>
public interface IGatewayPagamento
{
    /// <summary>Nome usado na rota do webhook (<c>/webhooks/pagamentos/{provedor}</c>) — minúsculo.</summary>
    string Provedor { get; }

    /// <summary>Falso no <c>Manual</c>: o endpoint de webhook responde 404 para ele.</summary>
    bool AceitaWebhook { get; }

    /// <summary>Devolve o ID do cliente no gateway, ou nulo se o gateway não tem esse conceito.</summary>
    Task<string?> CriarClienteAsync(DadosClienteGateway dados, CancellationToken cancellationToken = default);

    /// <summary>Devolve o ID da assinatura no gateway, ou nulo.</summary>
    Task<string?> CriarOuAlterarAssinaturaAsync(DadosAssinaturaGateway dados, CancellationToken cancellationToken = default);

    Task CancelarAssinaturaAsync(string? idExternoAssinatura, CancellationToken cancellationToken = default);

    /// <summary>Link de pagamento, ou instruções (PIX/contato) quando não há link.</summary>
    Task<InstrucoesPagamento> GerarLinkPagamentoAsync(DadosCobrancaGateway dados, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida a assinatura do provedor e interpreta o evento, conferindo os valores com o
    /// próprio provedor (nunca confia só no corpo — seção 8.6.6). Nulo = assinatura inválida.
    /// </summary>
    Task<EventoPagamentoGateway?> InterpretarWebhookAsync(RequisicaoWebhook requisicao, CancellationToken cancellationToken = default);
}

public sealed record DadosClienteGateway(Guid NegocioId, string NomeNegocio, string Email, string? Telefone);

public sealed record DadosAssinaturaGateway(Guid NegocioId, string? IdExternoCliente, string NomePlano, string Periodicidade, decimal ValorDoPeriodo);

public sealed record DadosCobrancaGateway(Guid NegocioId, string NomePlano, string Periodicidade, decimal Valor);

public sealed record InstrucoesPagamento(string? Link, string? ChavePix, string? WhatsAppContato, string Texto);

public sealed record RequisicaoWebhook(IReadOnlyDictionary<string, string> Cabecalhos, string Corpo);

public enum TipoEventoPagamento
{
    PagamentoConfirmado,
    AssinaturaCancelada,
    Ignorado,
}

public sealed record EventoPagamentoGateway(
    string IdEvento,
    TipoEventoPagamento Tipo,
    string? IdExternoAssinatura,
    string? IdExternoCobranca,
    decimal? Valor,
    DateTimeOffset? PagoEm,
    DateTimeOffset? PeriodoInicio,
    DateTimeOffset? PeriodoFim);
