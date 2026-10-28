using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Retenção LGPD das conversas: apaga de <c>mensagens_whatsapp</c> o que passou de <c>Ai__RetencaoMensagensDias</c>, em lotes. Com 0 (padrão)
/// não faz nada — apagar é irreversível e só liga por decisão do dono. Só mensagens: a linha da conversa, as pendências, <c>usos_ia</c> e
/// <c>rodadas_agente</c> ficam. Varre negócio a negócio pelo índice (negocio_id, criado_em) e registra apenas a quantidade removida.
/// </summary>
public sealed class JobLimparConversasWhatsApp
{
    public const int TamanhoDoLote = 1000;

    private readonly PlataformaDbContext _dbContext;
    private readonly OpcoesIa _opcoes;
    private readonly ILogger<JobLimparConversasWhatsApp> _logger;

    public JobLimparConversasWhatsApp(PlataformaDbContext dbContext, IOptions<OpcoesIa> opcoes, ILogger<JobLimparConversasWhatsApp> logger)
    {
        _dbContext = dbContext;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public Task<int> ExecutarAsync(CancellationToken cancellationToken = default) =>
        ExecutarAsync(DateTimeOffset.UtcNow, cancellationToken);

    public async Task<int> ExecutarAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        if (_opcoes.RetencaoMensagensDias <= 0)
            return 0;

        var limite = agora.AddDays(-_opcoes.RetencaoMensagensDias);
        var removidas = 0;

        var negocios = await _dbContext.Negocios.AsNoTracking().Select(n => n.Id).ToListAsync(cancellationToken);
        foreach (var negocioId in negocios)
        {
            while (true)
            {
                var ids = await _dbContext.MensagensWhatsApp.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => m.NegocioId == negocioId && m.CriadoEm < limite)
                    .OrderBy(m => m.CriadoEm)
                    .Select(m => m.Id)
                    .Take(TamanhoDoLote)
                    .ToListAsync(cancellationToken);
                if (ids.Count == 0)
                    break;

                removidas += await _dbContext.MensagensWhatsApp.IgnoreQueryFilters()
                    .Where(m => m.NegocioId == negocioId && ids.Contains(m.Id) && m.CriadoEm < limite)
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        if (removidas > 0)
            _logger.LogInformation("Agente: {Removidas} mensagem(ns) de conversa removida(s) pela retenção.", removidas);

        return removidas;
    }
}
