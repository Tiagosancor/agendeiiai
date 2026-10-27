using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Expira reservas vencidas em todo o sistema (seção 8.2.2) — roda periodicamente via
/// Hangfire, independente de alguém tentar criar um agendamento novo naquele profissional
/// (que também expira sob demanda, na própria transação — <see cref="ServicoAgendamentos"/>).
/// Sem isso, um horário reservado e abandonado ficaria bloqueado até alguém mexer
/// justamente naquele profissional.
/// </summary>
public sealed class JobExpirarReservas
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IServiceScopeFactory _escopos;
    private readonly ILogger<JobExpirarReservas> _logger;

    public JobExpirarReservas(PlataformaDbContext dbContext, IServiceScopeFactory escopos, ILogger<JobExpirarReservas> logger)
    {
        _dbContext = dbContext;
        _escopos = escopos;
        _logger = logger;
    }

    public async Task ExecutarAsync(CancellationToken cancellationToken = default)
    {
        var agora = DateTimeOffset.UtcNow;

        await ExpirarPendenciasDoAgenteAsync(agora, cancellationToken);

        // IgnoreQueryFilters: é uma rotina de manutenção que varre TODOS os negócios, não
        // só um (não há um IContextoNegocio resolvido aqui — é um job de background).
        await _dbContext.Agendamentos
            .IgnoreQueryFilters()
            .Where(a => a.Status == StatusAgendamento.Reservado && a.ReservadoAte < agora)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, StatusAgendamento.Expirado)
                .SetProperty(a => a.ReservadoAte, (DateTimeOffset?)null), cancellationToken);
    }

    /// <summary>
    /// Pendências do agente (Fase 3) que ficaram <c>Aguardando</c> além do prazo: expiram aqui, sem depender de nova mensagem do
    /// cliente (modo desligado, abandono). Antes da expiração em massa abaixo, para manter a ordem pendência e depois agendamento.
    /// Um escopo por negócio; falha de um não impede os outros nem a expiração dos agendamentos.
    /// </summary>
    private async Task ExpirarPendenciasDoAgenteAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var negocios = await _dbContext.PendenciasAgendamentoWhatsApp.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando && p.ExpiraEm <= agora)
            .Select(p => p.NegocioId).Distinct().OrderBy(id => id)
            .ToListAsync(cancellationToken);

        foreach (var negocioId in negocios)
        {
            try
            {
                using var escopo = _escopos.CreateScope();
                await escopo.ServiceProvider.GetRequiredService<ExpiradorPendenciasAgenteDoNegocio>()
                    .ExpirarAsync(negocioId, agora, cancellationToken);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                _logger.LogError("Falha ao expirar as pendências do agente do negócio {NegocioId} ({TipoErro}).", negocioId, excecao.GetType().Name);
            }
        }
    }
}
