using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Notificacoes.Outbox;

/// <summary>
/// Retenção da outbox: apaga, em lotes, os itens já terminados (enviados, cancelados, desconhecidos e falhos) cuja última atualização
/// passou de <see cref="OpcoesNotificacoes.RetencaoDias"/> (30). Itens pendentes ou em envio NUNCA são apagados, qualquer que seja a idade.
/// Registra só a quantidade removida.
/// </summary>
public sealed class JobLimparNotificacoes
{
    public const int TamanhoDoLote = 1000;

    private static readonly StatusNotificacao[] Terminais =
        [StatusNotificacao.Enviada, StatusNotificacao.Cancelada, StatusNotificacao.Desconhecida, StatusNotificacao.Falhou];

    private readonly PlataformaDbContext _dbContext;
    private readonly OpcoesNotificacoes _opcoes;
    private readonly TimeProvider _relogio;
    private readonly ILogger<JobLimparNotificacoes> _logger;

    public JobLimparNotificacoes(
        PlataformaDbContext dbContext, IOptions<OpcoesNotificacoes> opcoes, TimeProvider relogio, ILogger<JobLimparNotificacoes> logger)
    {
        _dbContext = dbContext;
        _opcoes = opcoes.Value;
        _relogio = relogio;
        _logger = logger;
    }

    public async Task<int> ExecutarAsync(CancellationToken cancellationToken = default)
    {
        var limite = _relogio.GetUtcNow().AddDays(-_opcoes.RetencaoDias).ToUniversalTime();
        var removidas = 0;

        while (true)
        {
            var ids = await _dbContext.NotificacoesPendentes.IgnoreQueryFilters().AsNoTracking()
                .Where(n => Terminais.Contains(n.Status) && n.AtualizadoEm < limite)
                .OrderBy(n => n.AtualizadoEm)
                .Select(n => n.Id)
                .Take(TamanhoDoLote)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
                break;

            // O estado é conferido de novo no DELETE: um item nunca terminal não some por uma corrida.
            removidas += await _dbContext.NotificacoesPendentes.IgnoreQueryFilters()
                .Where(n => ids.Contains(n.Id) && Terminais.Contains(n.Status) && n.AtualizadoEm < limite)
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (removidas > 0)
            _logger.LogInformation("Outbox: {Removidas} notificação(ões) terminada(s) removida(s) pela retenção.", removidas);

        return removidas;
    }
}
