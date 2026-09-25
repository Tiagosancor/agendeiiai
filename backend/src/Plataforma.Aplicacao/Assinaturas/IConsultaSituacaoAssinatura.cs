using Plataforma.Dominio.Assinaturas;

namespace Plataforma.Aplicacao.Assinaturas;

/// <summary>
/// Estado da assinatura decidido sempre no servidor (seção 8.6.5). Se o job atrasou (API
/// hibernando), aplica na hora o que o tempo já decidiu — pelo mesmo <c>ServicoAssinatura</c>.
/// </summary>
public interface IConsultaSituacaoAssinatura
{
    /// <summary>Nulo quando o negócio não tem assinatura (não acontece fora de testes: cadastro e migration sempre criam uma).</summary>
    Task<SituacaoAssinatura?> ObterAsync(Guid negocioId, CancellationToken cancellationToken = default);
}

public sealed record SituacaoAssinatura(EstadoAssinatura Estado, bool PermiteOperar);
