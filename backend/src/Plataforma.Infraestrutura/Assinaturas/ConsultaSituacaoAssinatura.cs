using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Assinaturas;

public sealed class ConsultaSituacaoAssinatura : IConsultaSituacaoAssinatura
{
    private readonly PlataformaDbContext _dbContext;
    private readonly Dictionary<Guid, SituacaoAssinatura?> _cacheDaRequisicao = [];

    public ConsultaSituacaoAssinatura(PlataformaDbContext dbContext) => _dbContext = dbContext;

    public async Task<SituacaoAssinatura?> ObterAsync(Guid negocioId, CancellationToken cancellationToken = default)
    {
        if (_cacheDaRequisicao.TryGetValue(negocioId, out var emCache))
            return emCache;

        // Explícito por negócio: também é chamado antes do tenant estar resolvido (middleware público).
        var assinatura = await _dbContext.Assinaturas.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.NegocioId == negocioId, cancellationToken);

        if (assinatura is not null && ServicoAssinatura.AtualizarPorTempo(assinatura, DateTimeOffset.UtcNow))
        {
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outra requisição (ou o job) aplicou a mesma transição antes — vale o que está no banco.
                _dbContext.ChangeTracker.Clear();
                assinatura = await _dbContext.Assinaturas.IgnoreQueryFilters().AsNoTracking()
                    .FirstAsync(a => a.NegocioId == negocioId, cancellationToken);
            }
        }

        var situacao = assinatura is null ? null : new SituacaoAssinatura(assinatura.Estado, assinatura.PermiteOperar);
        _cacheDaRequisicao[negocioId] = situacao;
        return situacao;
    }
}
