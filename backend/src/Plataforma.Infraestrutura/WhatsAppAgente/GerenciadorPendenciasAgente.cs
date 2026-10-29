using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

public enum DesfechoPendenciaAgente
{
    Recusada = 1,
    Substituida = 2,
    Falhou = 3,
    Expirada = 4,
}

/// <summary>
/// Cria e encerra as pendências de agendamento do agente (Fase 3) e libera a reserva do horário. Toda mudança de estado segue a
/// ordem de travas da Fatia 2: PENDÊNCIA primeiro, AGENDAMENTO depois (a confirmação e o job de expiração fazem igual), cada
/// <c>FOR UPDATE</c> com o negócio do contexto. Só atua em pendência ainda <c>Aguardando</c>; qualquer outro estado é intocável.
/// </summary>
public sealed class GerenciadorPendenciasAgente
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly ILogger<GerenciadorPendenciasAgente> _logger;

    public GerenciadorPendenciasAgente(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, ILogger<GerenciadorPendenciasAgente> logger)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _logger = logger;
    }

    public Task<PendenciaAgendamentoWhatsApp?> ObterAguardandoAsync(Guid conversaId, CancellationToken cancellationToken = default) =>
        _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ConversaId == conversaId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando, cancellationToken);

    /// <summary>
    /// Grava a pendência de uma reserva recém-feita. Só o índice único parcial (uma <c>Aguardando</c> por conversa) pode recusar:
    /// nesse caso devolve nulo e a reserva fica órfã até expirar (10 min, job existente).
    /// </summary>
    public async Task<PendenciaAgendamentoWhatsApp?> CriarAsync(
        Guid conversaId, Guid agendamentoId, DateTimeOffset agora, DateTimeOffset expiraEm, string? nomeInformado,
        CancellationToken cancellationToken = default)
    {
        var pendencia = new PendenciaAgendamentoWhatsApp(
            _contextoNegocio.NegocioId!.Value, conversaId, agendamentoId, agora, expiraEm, nomeInformado);
        _dbContext.PendenciasAgendamentoWhatsApp.Add(pendencia);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return pendencia;
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            _dbContext.Entry(pendencia).State = EntityState.Detached;
            _logger.LogWarning("Agente: já existe pendência aguardando nesta conversa (conversa {ConversaId}).", conversaId);
            return null;
        }
    }

    /// <summary>
    /// Encerra a pendência (se ainda <c>Aguardando</c>) e, no mesmo commit, expira a reserva do agente que ninguém tomou. Devolve
    /// verdadeiro se foi esta chamada que encerrou.
    /// </summary>
    public async Task<bool> EncerrarAsync(
        Guid pendenciaId, DesfechoPendenciaAgente desfecho, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await estrategia.ExecuteAsync(async () =>
            {
                await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                var pendencia = await _dbContext.PendenciasAgendamentoWhatsApp.FirstOrDefaultAsync(p => p.Id == pendenciaId, cancellationToken);
                if (pendencia is null)
                {
                    await transacao.RollbackAsync(cancellationToken);
                    return false;
                }

                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM pendencias_agendamento_whatsapp WHERE id = {pendenciaId} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);
                await _dbContext.Entry(pendencia).ReloadAsync(cancellationToken);

                if (!pendencia.Aguardando)
                {
                    await transacao.RollbackAsync(cancellationToken);
                    return false;
                }

                var agendamento = await _dbContext.Agendamentos.FirstOrDefaultAsync(a => a.Id == pendencia.AgendamentoId, cancellationToken);
                if (agendamento is not null)
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT 1 FROM agendamentos WHERE id = {agendamento.Id} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);
                    await _dbContext.Entry(agendamento).ReloadAsync(cancellationToken);

                    // Só a reserva que o agente fez e ninguém tomou: nunca um agendamento já confirmado ou de outro cliente.
                    if (agendamento.Status == StatusAgendamento.Reservado && agendamento.ClienteId is null
                        && agendamento.Origem == OrigemAgendamento.WhatsApp)
                        agendamento.Expirar();
                }

                var agora = DateTimeOffset.UtcNow;
                switch (desfecho)
                {
                    case DesfechoPendenciaAgente.Recusada: pendencia.Recusar(agora); break;
                    case DesfechoPendenciaAgente.Substituida: pendencia.Substituir(agora); break;
                    case DesfechoPendenciaAgente.Falhou: pendencia.Falhar(agora); break;
                    default: pendencia.Expirar(agora); break;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transacao.CommitAsync(cancellationToken);
                return true;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra transição da mesma pendência (confirmação, job) venceu: vale o que ela gravou.
            _dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    // ---------------------------------------------------------------- pendências de gestão (Fase 4, Fatia 7)

    public Task<PendenciaGestaoWhatsApp?> ObterGestaoAguardandoAsync(Guid conversaId, CancellationToken cancellationToken = default) =>
        _dbContext.PendenciasGestaoWhatsApp.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ConversaId == conversaId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando, cancellationToken);

    /// <summary>
    /// Cria a pendência de gestão da conversa e, NA MESMA TRANSAÇÃO, substitui o que a conversa tinha aberto: a pendência de agendamento
    /// (liberando a reserva do horário) e a pendência de gestão anterior. Ordem de travas: pendência de agendamento -> o agendamento
    /// reservado; depois a pendência de gestão antiga (outra tabela, sem agendamento a liberar). Só uma pendência de cada tipo existe
    /// aberta por conversa (índices únicos parciais). Nulo = o índice recusou (corrida) ou outra transição venceu: nada foi criado.
    /// </summary>
    public async Task<PendenciaGestaoWhatsApp?> CriarGestaoAsync(
        Guid conversaId, Guid agendamentoId, TipoGestaoWhatsApp tipo, DateTimeOffset agora, DateTimeOffset expiraEm, DateTimeOffset? novoInicio = null,
        CancellationToken cancellationToken = default, Guid? profissionalId = null, DateTimeOffset? inicioAnterior = null, int? duracaoMinutos = null)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        PendenciaGestaoWhatsApp? criada = null;
        try
        {
            await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                criada = null;
                await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                var idAgendamentoAberta = await _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking()
                    .Where(p => p.ConversaId == conversaId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando)
                    .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
                if (idAgendamentoAberta is { } idAg)
                {
                    var pendenciaAg = await _dbContext.PendenciasAgendamentoWhatsApp.FirstAsync(p => p.Id == idAg, cancellationToken);
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT 1 FROM pendencias_agendamento_whatsapp WHERE id = {idAg} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);
                    await _dbContext.Entry(pendenciaAg).ReloadAsync(cancellationToken);
                    if (pendenciaAg.Aguardando)
                    {
                        var reserva = await _dbContext.Agendamentos.FirstOrDefaultAsync(a => a.Id == pendenciaAg.AgendamentoId, cancellationToken);
                        if (reserva is not null)
                        {
                            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                                $"SELECT 1 FROM agendamentos WHERE id = {reserva.Id} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);
                            await _dbContext.Entry(reserva).ReloadAsync(cancellationToken);
                            // Só a reserva que o agente fez e ninguém tomou: nunca um agendamento já confirmado ou de outro cliente.
                            if (reserva.Status == StatusAgendamento.Reservado && reserva.ClienteId is null && reserva.Origem == OrigemAgendamento.WhatsApp)
                                reserva.Expirar();
                        }

                        pendenciaAg.Substituir(agora);
                    }
                }

                var idGestaoAberta = await _dbContext.PendenciasGestaoWhatsApp.AsNoTracking()
                    .Where(p => p.ConversaId == conversaId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando)
                    .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
                if (idGestaoAberta is { } idGe)
                {
                    var antiga = await _dbContext.PendenciasGestaoWhatsApp.FirstAsync(p => p.Id == idGe, cancellationToken);
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT 1 FROM pendencias_gestao_whatsapp WHERE id = {idGe} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);
                    await _dbContext.Entry(antiga).ReloadAsync(cancellationToken);
                    if (antiga.Aguardando)
                        antiga.Substituir(agora);
                }

                // As substituições gravam antes do insert: o índice único parcial (uma Aguardando por conversa) não pode ver as duas.
                await _dbContext.SaveChangesAsync(cancellationToken);

                var nova = new PendenciaGestaoWhatsApp(negocioId, conversaId, agendamentoId, tipo, agora, expiraEm, novoInicio, profissionalId, inicioAnterior, duracaoMinutos);
                _dbContext.PendenciasGestaoWhatsApp.Add(nova);
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch
                {
                    _dbContext.Entry(nova).State = EntityState.Detached;
                    throw;
                }

                await transacao.CommitAsync(cancellationToken);
                criada = nova;
            });
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            _dbContext.ChangeTracker.Clear();
            _logger.LogWarning("Agente: já existe pendência de gestão aguardando nesta conversa (conversa {ConversaId}).", conversaId);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return null;
        }

        return criada;
    }

    /// <summary>Encerra a pendência de gestão (se ainda <c>Aguardando</c>) sob a trava dela. Verdadeiro se foi esta chamada que encerrou.</summary>
    public async Task<bool> EncerrarGestaoAsync(
        Guid pendenciaId, DesfechoPendenciaAgente desfecho, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        try
        {
            return await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                var pendencia = await _dbContext.PendenciasGestaoWhatsApp.FirstOrDefaultAsync(p => p.Id == pendenciaId, cancellationToken);
                if (pendencia is null)
                {
                    await transacao.RollbackAsync(cancellationToken);
                    return false;
                }

                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM pendencias_gestao_whatsapp WHERE id = {pendenciaId} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);
                await _dbContext.Entry(pendencia).ReloadAsync(cancellationToken);

                if (!pendencia.Aguardando)
                {
                    await transacao.RollbackAsync(cancellationToken);
                    return false;
                }

                var agora = DateTimeOffset.UtcNow;
                switch (desfecho)
                {
                    case DesfechoPendenciaAgente.Recusada: pendencia.Recusar(agora); break;
                    case DesfechoPendenciaAgente.Substituida: pendencia.Substituir(agora); break;
                    case DesfechoPendenciaAgente.Falhou: pendencia.Falhar(agora); break;
                    default: pendencia.Expirar(agora); break;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transacao.CommitAsync(cancellationToken);
                return true;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra transição da mesma pendência (confirmação, job) venceu: vale o que ela gravou.
            _dbContext.ChangeTracker.Clear();
            return false;
        }
    }
}
