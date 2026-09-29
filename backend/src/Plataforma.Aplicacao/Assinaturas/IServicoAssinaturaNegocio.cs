using Plataforma.Aplicacao.Administracao;
using Plataforma.Dominio.Assinaturas;

namespace Plataforma.Aplicacao.Assinaturas;

/// <summary>Assinatura vista pelo próprio negócio no painel (seção 7) — negócio sempre do JWT.</summary>
public interface IServicoAssinaturaNegocio
{
    Task<DetalheAssinaturaNegocio?> ObterAsync(CancellationToken cancellationToken = default);

    /// <summary>Para o aviso do topo do painel: últimos 7 dias antes do prazo, carência ou suspensão.</summary>
    Task<AvisoAssinatura?> ObterAvisoAsync(CancellationToken cancellationToken = default);

    Task<InstrucoesPagamento> ObterInstrucoesPagamentoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lança <c>LimiteProfissionaisExcedidoException</c> ao ir para um plano que não comporta os profissionais ativos. Já
    /// contratada no gateway, altera lá também (vale a partir da próxima cobrança).
    /// </summary>
    Task TrocarPlanoAsync(string autor, Guid planoId, Periodicidade periodicidade, CancellationToken cancellationToken = default);

    /// <summary>
    /// Contrata pelo gateway automático: cria o pagador (CPF/CNPJ) e a assinatura com a primeira cobrança no fim do teste ou
    /// do período já pago (hoje, se já passou), e devolve o link da cobrança. Lança <c>RegraAssinaturaException</c> (gateway
    /// manual, cancelada), <see cref="ArgumentException"/> (documento inválido) e <see cref="FalhaGatewayPagamentoException"/>.
    /// </summary>
    Task<InstrucoesPagamento> ContratarAsync(
        string autor, string emailTitular, Guid planoId, Periodicidade periodicidade, string cpfCnpj,
        CancellationToken cancellationToken = default);

    /// <summary>Usa até o fim do período pago (ou do teste) e não gera cobrança nova (decisão do dono). Devolve se cancelou na hora.</summary>
    Task<bool> CancelarAsync(string autor, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>PagamentoAutomatico</c>: o gateway configurado cobra sozinho (a tela mostra "Contratar" em vez das instruções de PIX).
/// <c>Contratada</c>: já tem assinatura no gateway. <c>CancelamentoAte</c>: cancelamento pedido, vale até essa data.
/// </summary>
public sealed record DetalheAssinaturaNegocio(
    Guid PlanoId, string Plano, Periodicidade Periodicidade, EstadoAssinatura Estado, decimal PrecoMensalTravado,
    decimal ValorDoPeriodo, DateTimeOffset? FimTeste, DateTimeOffset? ProximoVencimento, DateTimeOffset? CarenciaAte,
    int ProfissionaisAtivos, int MaximoProfissionais, IReadOnlyList<CobrancaAssinaturaDto> Cobrancas,
    bool PagamentoAutomatico = false, bool Contratada = false, string? DocumentoTitular = null, DateTimeOffset? CancelamentoAte = null);

public sealed record AvisoAssinatura(EstadoAssinatura Estado, DateTimeOffset? Prazo, int? DiasRestantes, bool Destacado);
