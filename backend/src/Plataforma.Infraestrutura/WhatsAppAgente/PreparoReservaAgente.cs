using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Preparo de uma reserva do agente de WhatsApp que roda DENTRO da transação da reserva (Fase 4): no mesmo commit, a pendência de
/// agendamento anterior da conversa (e a reserva do horário dela) e o pedido de gestão aberto são encerrados como <c>Substituida</c> e a
/// nova pendência é gravada. Ordem de travas: advisory do profissional (já tomada por quem chama) -> pendência de agendamento -> o
/// agendamento reservado -> pendência de gestão -> insert; cada <c>FOR UPDATE</c> com <c>AND negocio_id</c>. Se a reserva falhar por
/// conflito, exclusion constraint ou erro, tudo desfaz e a conversa continua com o que tinha.
/// </summary>
public sealed class PreparoReservaAgente : IPreparoReservaAgente
{
    private readonly PlataformaDbContext _dbContext;
    private readonly Guid _negocioId;
    private readonly Guid _conversaId;
    private readonly string? _nomeInformado;

    public PreparoReservaAgente(PlataformaDbContext dbContext, Guid negocioId, Guid conversaId, string? nomeInformado)
    {
        _dbContext = dbContext;
        _negocioId = negocioId;
        _conversaId = conversaId;
        _nomeInformado = nomeInformado;
    }

    /// <summary>A pendência criada pela última tentativa da transação (nula se ela não chegou a ser montada).</summary>
    public PendenciaAgendamentoWhatsApp? Pendencia { get; private set; }

    public async Task AntesDeInserirAsync(CancellationToken cancellationToken)
    {
        Pendencia = null;
        var agora = DateTimeOffset.UtcNow;

        var idAgendamento = await _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking()
            .Where(p => p.ConversaId == _conversaId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando)
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (idAgendamento is { } idAg)
        {
            var pendencia = await _dbContext.PendenciasAgendamentoWhatsApp.FirstAsync(p => p.Id == idAg, cancellationToken);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM pendencias_agendamento_whatsapp WHERE id = {idAg} AND negocio_id = {_negocioId} FOR UPDATE", cancellationToken);
            await _dbContext.Entry(pendencia).ReloadAsync(cancellationToken);
            if (pendencia.Aguardando)
            {
                var reserva = await _dbContext.Agendamentos.FirstOrDefaultAsync(a => a.Id == pendencia.AgendamentoId, cancellationToken);
                if (reserva is not null)
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT 1 FROM agendamentos WHERE id = {reserva.Id} AND negocio_id = {_negocioId} FOR UPDATE", cancellationToken);
                    await _dbContext.Entry(reserva).ReloadAsync(cancellationToken);
                    // Só a reserva que o agente fez e ninguém tomou: nunca um agendamento já confirmado ou de outro cliente.
                    if (reserva.Status == StatusAgendamento.Reservado && reserva.ClienteId is null && reserva.Origem == OrigemAgendamento.WhatsApp)
                        reserva.Expirar();
                }

                pendencia.Substituir(agora);
            }
        }

        var idGestao = await _dbContext.PendenciasGestaoWhatsApp.AsNoTracking()
            .Where(p => p.ConversaId == _conversaId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando)
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (idGestao is { } idGe)
        {
            var gestao = await _dbContext.PendenciasGestaoWhatsApp.FirstAsync(p => p.Id == idGe, cancellationToken);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM pendencias_gestao_whatsapp WHERE id = {idGe} AND negocio_id = {_negocioId} FOR UPDATE", cancellationToken);
            await _dbContext.Entry(gestao).ReloadAsync(cancellationToken);
            if (gestao.Aguardando)
                gestao.Substituir(agora);
        }

        // As substituições gravam antes do insert: o índice único parcial (uma Aguardando por conversa) não pode ver as duas.
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public void DepoisDeAdicionar(Guid agendamentoId, DateTimeOffset reservadoAte)
    {
        var agora = DateTimeOffset.UtcNow;
        Pendencia = new PendenciaAgendamentoWhatsApp(_negocioId, _conversaId, agendamentoId, agora, reservadoAte, _nomeInformado);
        _dbContext.PendenciasAgendamentoWhatsApp.Add(Pendencia);
    }
}
