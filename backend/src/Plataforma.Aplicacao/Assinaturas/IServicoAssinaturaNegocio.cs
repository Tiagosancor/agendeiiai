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

    /// <summary>Lança <c>LimiteProfissionaisExcedidoException</c> ao ir para um plano que não comporta os profissionais ativos.</summary>
    Task TrocarPlanoAsync(string autor, Guid planoId, Periodicidade periodicidade, CancellationToken cancellationToken = default);
}

public sealed record DetalheAssinaturaNegocio(
    Guid PlanoId, string Plano, Periodicidade Periodicidade, EstadoAssinatura Estado, decimal PrecoMensalTravado,
    decimal ValorDoPeriodo, DateTimeOffset? FimTeste, DateTimeOffset? ProximoVencimento, DateTimeOffset? CarenciaAte,
    int ProfissionaisAtivos, int MaximoProfissionais, IReadOnlyList<CobrancaAssinaturaDto> Cobrancas);

public sealed record AvisoAssinatura(EstadoAssinatura Estado, DateTimeOffset? Prazo, int? DiasRestantes, bool Destacado);
