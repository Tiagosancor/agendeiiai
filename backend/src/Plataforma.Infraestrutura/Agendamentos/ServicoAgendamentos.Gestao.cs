using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.WhatsAppAgente;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Gestão dos agendamentos do cliente pelo agente de WhatsApp (Fase 4). Métodos internos, sem controller: o modelo nunca chama nada
/// daqui, e nenhum recebe motivo de forçar (impossível por tipo).
/// </summary>
public sealed partial class ServicoAgendamentos
{
    public async Task<ResultadoGestaoAgente> CancelarPeloAgenteAsync(CancelarPeloAgente dados, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;

        if (!TelefoneE164.TentarCriar(dados.TelefoneConversa, out var telefone) || telefone is null)
            return ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.NaoEncontrada);

        // Assinatura suspensa ou serviço de IA desligado depois do resumo: o "sim" não cancela (a pendência expira pelo job).
        if (await _assinaturas.ObterAsync(negocioId, cancellationToken) is { PermiteOperar: false })
            return ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.AssinaturaSuspensa);
        if (!await PoliticaAgenteIa.LerContratadoAsync(_dbContext, negocioId, cancellationToken))
            return ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.ServicoNaoContratado);

        var usaOutbox = _outbox.Usa(FamiliaNotificacao.Operacional);
        var resultado = ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.NaoEncontrada);
        Guid? agendamentoNotificar = null;

        try
        {
            await ExecutarNaTravaDeQuinzenasAsync(async _ =>
            {
                resultado = ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.NaoEncontrada);
                agendamentoNotificar = null;

                var pendencia = await _dbContext.PendenciasGestaoWhatsApp.FirstOrDefaultAsync(p => p.Id == dados.PendenciaGestaoId, cancellationToken);
                if (pendencia is null || pendencia.Tipo != TipoGestaoWhatsApp.Cancelar)
                    return false;

                // A pendência é da conversa deste telefone, nunca de outra (mesmo negócio ou não).
                var conversa = await _dbContext.ConversasWhatsApp.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pendencia.ConversaId, cancellationToken);
                if (conversa is null || !TelefoneE164.TentarCriar(conversa.TelefoneCliente, out var telefoneDaConversa)
                    || !telefone.Equals(telefoneDaConversa))
                    return false;

                // Ordem global de travas: (advisory de quinzenas, já tomada) pendência -> agendamento -> cliente.
                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM pendencias_gestao_whatsapp WHERE id = {pendencia.Id} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);
                await _dbContext.Entry(pendencia).ReloadAsync(cancellationToken);

                if (pendencia.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada)
                {
                    // Repetição (segundo "sim", ou nova tentativa depois de COMMIT ambíguo): só quem gravou é "quem efetivou agora".
                    var fuiEu = pendencia.EfetivadaPor(dados.OperacaoId);
                    resultado = ResultadoGestaoAgente.Efetivada(pendencia.AgendamentoId, fuiEu);
                    if (fuiEu)
                        agendamentoNotificar = pendencia.AgendamentoId;
                    return false;
                }

                if (!pendencia.Aguardando)
                {
                    resultado = ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.Encerrada, pendencia.AgendamentoId);
                    return false;
                }

                var agendamento = await _dbContext.Agendamentos.Include(a => a.Servicos)
                    .FirstOrDefaultAsync(a => a.Id == pendencia.AgendamentoId && a.NegocioId == negocioId, cancellationToken);
                if (agendamento is null)
                    return await RecusarGestaoAsync(pendencia, MotivoRecusaGestaoAgente.NaoPermitido, null, cancellationToken, r => resultado = r);

                await TravarLinhaERelerAsync(agendamento, cancellationToken);

                // O instante vale DEPOIS de ter as travas e o estado relido: a espera pode atravessar o prazo.
                var agora = DateTimeOffset.UtcNow;
                if (pendencia.Vencida(agora))
                    return await RecusarGestaoAsync(pendencia, MotivoRecusaGestaoAgente.Expirada, agendamento.Id, cancellationToken, r => resultado = r, expirar: true);

                // Cliente do agendamento, travado e relido (a anonimização/exclusão e a troca de telefone tomam a mesma linha): G1 refeito aqui.
                Plataforma.Dominio.Clientes.Cliente? cliente = null;
                if (agendamento.ClienteId is { } clienteId)
                {
                    cliente = await _dbContext.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId && c.NegocioId == negocioId, cancellationToken);
                    if (cliente is not null)
                    {
                        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT 1 FROM clientes WHERE id = {cliente.Id} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);
                        await _dbContext.Entry(cliente).ReloadAsync(cancellationToken);
                    }
                }

                var vinculoValido = cliente is { Excluido: false, Telefone: { } atual } && telefone.FormasEquivalentes().Contains(atual)
                    && agendamento.NegocioId == negocioId
                    && agendamento.Origem is OrigemAgendamento.LinkPublico or OrigemAgendamento.WhatsApp;
                if (!vinculoValido)
                    return await RecusarGestaoAsync(pendencia, MotivoRecusaGestaoAgente.NaoPermitido, agendamento.Id, cancellationToken, r => resultado = r);

                agora = DateTimeOffset.UtcNow;
                if (pendencia.Vencida(agora))
                    return await RecusarGestaoAsync(pendencia, MotivoRecusaGestaoAgente.Expirada, agendamento.Id, cancellationToken, r => resultado = r, expirar: true);

                // As MESMAS regras do link público: status, antecedência mínima (conferida aqui, dentro da transação) e quinzena. Mais uma,
                // conservadora: com pagamento registrado o agente não cancela.
                if (agendamento.Status != StatusAgendamento.Agendado || !GestaoPublicaAgendamento.StatusPermiteCancelarPeloLink(agendamento))
                    return await RecusarGestaoAsync(pendencia, MotivoRecusaGestaoAgente.StatusNaoPermite, agendamento.Id, cancellationToken, r => resultado = r);

                if (GestaoPublicaAgendamento.MotivoTemporal(agendamento.Inicio, agora, _opcoesAgendamentoPublico.AntecedenciaMinimaCancelamentoHoras) is not null)
                    return await RecusarGestaoAsync(pendencia, MotivoRecusaGestaoAgente.AntecedenciaMinima, agendamento.Id, cancellationToken, r => resultado = r);

                if (await _dbContext.Pagamentos.AnyAsync(p => p.AgendamentoId == agendamento.Id && p.NegocioId == negocioId, cancellationToken))
                    return await RecusarGestaoAsync(pendencia, MotivoRecusaGestaoAgente.ComPagamento, agendamento.Id, cancellationToken, r => resultado = r);

                if (await _travaQuinzenas.AtendimentoTravadoAsync(agendamento.ProfissionalId, agendamento.Inicio, cancellationToken))
                    return await RecusarGestaoAsync(pendencia, MotivoRecusaGestaoAgente.QuinzenaFechada, agendamento.Id, cancellationToken, r => resultado = r);

                // Cancelamento normal (aviso ao profissional, mensagens obsoletas); o cliente é avisado só pelo texto da conversa.
                await CancelarCarregadoAsync(agendamento, usaOutbox, avisandoCliente: false, cancellationToken);
                pendencia.Efetivar(agora, dados.MensagemConfirmacaoId, dados.OperacaoId);
                await RegistrarAuditoriaCancelamentoPeloAgenteAsync(agendamento, pendencia.Id, cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);
                resultado = ResultadoGestaoAgente.Efetivada(agendamento.Id, true);
                agendamentoNotificar = agendamento.Id;
                return true;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra transição da mesma pendência (assumir, expiração, preparo novo) venceu: vale o que ela gravou.
            _dbContext.ChangeTracker.Clear();
            return ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.Encerrada);
        }

        // Sem outbox, o aviso ao profissional sai agora, depois do commit.
        if (resultado.Sucesso && resultado.EfetivadaAgora && !usaOutbox && agendamentoNotificar is { } id)
            await NotificarProfissionalAsync(id, EventoAgendamentoProfissional.Cancelado, cancellationToken);

        return resultado;
    }

    /// <summary>Encerra a pendência (Falhou, ou Expirada) na própria transação e devolve o motivo; o commit é de quem chamou.</summary>
    private async Task<bool> RecusarGestaoAsync(
        PendenciaGestaoWhatsApp pendencia, MotivoRecusaGestaoAgente motivo, Guid? agendamentoId, CancellationToken cancellationToken,
        Action<ResultadoGestaoAgente> definir, bool expirar = false)
    {
        var agora = DateTimeOffset.UtcNow;
        if (expirar)
            pendencia.Expirar(agora);
        else
            pendencia.Falhar(agora);

        await _dbContext.SaveChangesAsync(cancellationToken);
        definir(ResultadoGestaoAgente.Recusada(motivo, agendamentoId));
        return false;
    }

    /// <summary>Auditoria do cancelamento feito pelo agente (autor "sistema"): sem nome nem telefone do cliente.</summary>
    private async Task RegistrarAuditoriaCancelamentoPeloAgenteAsync(Agendamento agendamento, Guid pendenciaId, CancellationToken cancellationToken)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == agendamento.NegocioId, cancellationToken);
        var profissional = await _dbContext.Profissionais.AsNoTracking().FirstAsync(p => p.Id == agendamento.ProfissionalId, cancellationToken);
        var servicos = string.Join(", ", agendamento.Servicos.Select(s => s.Nome));

        _auditoria.Registrar(
            AcoesAuditoria.CancelarPeloWhatsApp, "Agendamento", agendamento.Id,
            $"pendência {pendenciaId}; serviço {servicos}; profissional {profissional.Nome}; início {FormatacaoBrasil.DataHora(agendamento.Inicio, negocio.Fuso)}");
    }
}
