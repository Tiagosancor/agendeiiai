using Microsoft.EntityFrameworkCore;
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

    public JobExpirarReservas(PlataformaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task ExecutarAsync(CancellationToken cancellationToken = default)
    {
        var agora = DateTimeOffset.UtcNow;

        // IgnoreQueryFilters: é uma rotina de manutenção que varre TODOS os negócios, não
        // só um (não há um IContextoNegocio resolvido aqui — é um job de background).
        await _dbContext.Agendamentos
            .IgnoreQueryFilters()
            .Where(a => a.Status == StatusAgendamento.Reservado && a.ReservadoAte < agora)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, StatusAgendamento.Expirado)
                .SetProperty(a => a.ReservadoAte, (DateTimeOffset?)null), cancellationToken);
    }
}
