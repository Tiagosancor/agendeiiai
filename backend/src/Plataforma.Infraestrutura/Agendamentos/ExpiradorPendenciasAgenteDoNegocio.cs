using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Expira as pendências de agendamento do agente de WhatsApp que ficaram <c>Aguardando</c> além do prazo (o cliente sumiu, ou o
/// modo foi desligado) de UM negócio. Resolvido num escopo próprio por <see cref="JobExpirarReservas"/>, com o negócio definido.
/// Cada pendência é uma transação: trava a pendência, relê, trava o agendamento (mesma ordem da confirmação: pendência e depois
/// agendamento) e só então decide. Pendência em qualquer outro estado nunca é tocada.
/// </summary>
public sealed class ExpiradorPendenciasAgenteDoNegocio
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public ExpiradorPendenciasAgenteDoNegocio(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<int> ExpirarAsync(Guid negocioId, DateTimeOffset agora, CancellationToken cancellationToken = default)
    {
        _contextoNegocio.Definir(negocioId);

        var ids = await _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking()
            .Where(p => p.NegocioId == negocioId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando && p.ExpiraEm <= agora)
            .OrderBy(p => p.ExpiraEm).Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var expiradas = 0;
        foreach (var id in ids)
        {
            var estrategia = _dbContext.Database.CreateExecutionStrategy();
            try
            {
                if (await estrategia.ExecuteAsync(() => ExpirarUmaAsync(id, agora, cancellationToken)))
                    expiradas++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outra transição da mesma pendência (confirmação, preparo novo) venceu: vale o que ela gravou.
                _dbContext.ChangeTracker.Clear();
            }
        }

        // Pedidos de gestão (cancelar): nada a liberar, só encerra o pedido (um "sim" tardio recebe o aviso de prazo, nunca cancela).
        var idsGestao = await _dbContext.PendenciasGestaoWhatsApp.AsNoTracking()
            .Where(p => p.NegocioId == negocioId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando && p.ExpiraEm <= agora)
            .OrderBy(p => p.ExpiraEm).Select(p => p.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in idsGestao)
        {
            var estrategia = _dbContext.Database.CreateExecutionStrategy();
            try
            {
                if (await estrategia.ExecuteAsync(() => ExpirarGestaoUmaAsync(id, agora, cancellationToken)))
                    expiradas++;
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        return expiradas;
    }

    private async Task<bool> ExpirarGestaoUmaAsync(Guid id, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var pendencia = await _dbContext.PendenciasGestaoWhatsApp.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pendencia is null)
        {
            await transacao.RollbackAsync(cancellationToken);
            return false;
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM pendencias_gestao_whatsapp WHERE id = {id} AND negocio_id = {_contextoNegocio.NegocioId!.Value} FOR UPDATE", cancellationToken);
        await _dbContext.Entry(pendencia).ReloadAsync(cancellationToken);

        if (!pendencia.Aguardando || pendencia.ExpiraEm > agora)
        {
            await transacao.RollbackAsync(cancellationToken);
            return false;
        }

        pendencia.Expirar(agora);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> ExpirarUmaAsync(Guid id, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var pendencia = await _dbContext.PendenciasAgendamentoWhatsApp.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pendencia is null)
        {
            await transacao.RollbackAsync(cancellationToken);
            return false;
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM pendencias_agendamento_whatsapp WHERE id = {id} AND negocio_id = {_contextoNegocio.NegocioId!.Value} FOR UPDATE", cancellationToken);
        await _dbContext.Entry(pendencia).ReloadAsync(cancellationToken);

        if (!pendencia.Aguardando || pendencia.ExpiraEm > agora)
        {
            await transacao.RollbackAsync(cancellationToken);
            return false;
        }

        var agendamento = await _dbContext.Agendamentos.FirstOrDefaultAsync(a => a.Id == pendencia.AgendamentoId, cancellationToken);
        if (agendamento is not null)
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM agendamentos WHERE id = {agendamento.Id} AND negocio_id = {_contextoNegocio.NegocioId!.Value} FOR UPDATE", cancellationToken);
            await _dbContext.Entry(agendamento).ReloadAsync(cancellationToken);

            // Só a reserva que o agente fez e ninguém tomou: nunca um agendamento já confirmado ou de outro cliente.
            if (agendamento.Status == StatusAgendamento.Reservado && agendamento.ClienteId is null
                && agendamento.Origem == OrigemAgendamento.WhatsApp)
                agendamento.Expirar();
        }

        pendencia.Expirar(agora);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return true;
    }
}
