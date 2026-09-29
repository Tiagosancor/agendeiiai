namespace Plataforma.Aplicacao.Assinaturas;

/// <summary>
/// Contrato do gateway de cobrança da assinatura (seção 7): <c>Manual</c> (PIX combinado por fora) e <c>Asaas</c>
/// (assinatura recorrente com fatura hospedada — nenhum dado de cartão passa por aqui). Qual vale é configuração
/// (<c>Cobranca__Provedor</c>). Tudo que é específico do provedor fica na implementação; o webhook chega aqui já traduzido
/// em <see cref="EventoPagamentoGateway"/>. Falha de comunicação ou recusa do provedor lança
/// <see cref="FalhaGatewayPagamentoException"/>.
/// </summary>
public interface IGatewayPagamento
{
    /// <summary>Nome usado na rota do webhook (<c>/webhooks/pagamentos/{provedor}</c>) — minúsculo.</summary>
    string Provedor { get; }

    /// <summary>
    /// Falso no <c>Manual</c>: o endpoint de webhook responde 404 para ele. Verdadeiro = cobrança automática: o negócio
    /// contrata e paga sozinho, pelo link do provedor.
    /// </summary>
    bool AceitaWebhook { get; }

    /// <summary>Devolve o ID do cliente no gateway, ou nulo se o gateway não tem esse conceito.</summary>
    Task<string?> CriarClienteAsync(DadosClienteGateway dados, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sem <c>IdExternoAssinatura</c>, cria (primeira cobrança em <c>PrimeiroVencimento</c>); com ele, altera valor e ciclo,
    /// valendo a partir da próxima cobrança. Devolve o ID da assinatura no gateway, ou nulo.
    /// </summary>
    Task<string?> CriarOuAlterarAssinaturaAsync(DadosAssinaturaGateway dados, CancellationToken cancellationToken = default);

    Task CancelarAssinaturaAsync(string? idExternoAssinatura, CancellationToken cancellationToken = default);

    /// <summary>Link da cobrança em aberto, ou instruções (PIX/contato) quando não há link.</summary>
    Task<InstrucoesPagamento> GerarLinkPagamentoAsync(DadosCobrancaGateway dados, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida a assinatura do provedor e interpreta o evento, conferindo os valores com o
    /// próprio provedor (nunca confia só no corpo — seção 8.6.6). Nulo = assinatura inválida.
    /// </summary>
    Task<EventoPagamentoGateway?> InterpretarWebhookAsync(RequisicaoWebhook requisicao, CancellationToken cancellationToken = default);
}

/// <summary><c>CpfCnpj</c>: só dígitos; vai ao provedor e não é gravado nem logado aqui.</summary>
public sealed record DadosClienteGateway(Guid NegocioId, string NomeNegocio, string Email, string? Telefone, string? CpfCnpj = null);

public sealed record DadosAssinaturaGateway(
    Guid NegocioId, string? IdExternoCliente, string NomePlano, string Periodicidade, decimal ValorDoPeriodo,
    string? IdExternoAssinatura = null, DateTimeOffset? PrimeiroVencimento = null, Guid? AssinaturaId = null);

public sealed record DadosCobrancaGateway(
    Guid NegocioId, string NomePlano, string Periodicidade, decimal Valor, string? IdExternoAssinatura = null);

public sealed record InstrucoesPagamento(string? Link, string? ChavePix, string? WhatsAppContato, string Texto);

public sealed record RequisicaoWebhook(IReadOnlyDictionary<string, string> Cabecalhos, string Corpo);

public enum TipoEventoPagamento
{
    PagamentoConfirmado,
    AssinaturaCancelada,

    /// <summary>Cobrança venceu sem pagamento.</summary>
    CobrancaVencida,

    /// <summary>Pagamento estornado ou contestado (chargeback).</summary>
    PagamentoEstornado,

    Ignorado,
}

/// <summary>
/// Evento neutro. <c>PeriodoInicio</c>/<c>PeriodoFim</c> nulos: quem aplica calcula a partir do <c>Vencimento</c> e da
/// periodicidade da assinatura. <c>Nome</c>: o nome do evento no provedor, para o registro.
/// </summary>
public sealed record EventoPagamentoGateway(
    string IdEvento,
    TipoEventoPagamento Tipo,
    string? IdExternoAssinatura,
    string? IdExternoCobranca,
    decimal? Valor,
    DateTimeOffset? PagoEm,
    DateTimeOffset? PeriodoInicio,
    DateTimeOffset? PeriodoFim,
    DateTimeOffset? Vencimento = null,
    string? Forma = null,
    string? Nome = null);

/// <summary>
/// O provedor não respondeu ou recusou o pedido. <c>Recusado</c>: o provedor disse não (ex.: CPF inválido) e a
/// <c>Message</c> pode ir para a tela; senão é indisponibilidade, com mensagem genérica.
/// </summary>
public sealed class FalhaGatewayPagamentoException(string mensagem, bool recusado = false) : Exception(mensagem)
{
    public bool Recusado { get; } = recusado;
}
