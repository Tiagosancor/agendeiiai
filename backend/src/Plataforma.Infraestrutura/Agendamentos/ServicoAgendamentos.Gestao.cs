using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.Notificacoes;
using Plataforma.Infraestrutura.Persistencia;
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

    /// <summary>
    /// Remarcação pelo agente (Fatia 8). Ordem global de travas: advisory do PROFISSIONAL (o do retrato do resumo, como todo caminho que
    /// grava na agenda) -> pendência -> agendamento -> cliente, cada <c>FOR UPDATE</c> com <c>negocio_id</c>. Sem trava de quinzenas: remarcar
    /// não mexe em dinheiro (nem o painel nem o link a tomam). O novo horário NÃO foi segurado entre o resumo e o "SIM": é conferido aqui,
    /// depois das travas, na oferta pública, em forçados e pela exclusion constraint.
    /// </summary>
    public async Task<ResultadoGestaoAgente> RemarcarPeloAgenteAsync(RemarcarPeloAgente dados, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;

        if (!TelefoneE164.TentarCriar(dados.TelefoneConversa, out var telefone) || telefone is null)
            return ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.NaoEncontrada);

        if (await _assinaturas.ObterAsync(negocioId, cancellationToken) is { PermiteOperar: false })
            return ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.AssinaturaSuspensa);
        if (!await PoliticaAgenteIa.LerContratadoAsync(_dbContext, negocioId, cancellationToken))
            return ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.ServicoNaoContratado);

        var usaOutbox = _outbox.Usa(FamiliaNotificacao.Operacional);
        var resultado = ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.NaoEncontrada);
        Guid? agendamentoNotificar = null;
        Guid? falharDepois = null;
        Guid? profissionalDoConflito = null;
        DateTimeOffset novoInicioDoConflito = default;
        var duracaoDoConflito = 0;

        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        var tentativas = new TentativasDaTransacao(_dbContext);

        try
        {
            await estrategia.ExecuteAsync(async () =>
            {
                tentativas.Iniciar();
                resultado = ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.NaoEncontrada);
                agendamentoNotificar = null;
                falharDepois = null;

                await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                // Leitura sem trava só para saber o profissional do RETRATO do resumo (imutável na pendência): a trava dele vem primeiro.
                var previa = await _dbContext.PendenciasGestaoWhatsApp.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == dados.PendenciaGestaoId && p.NegocioId == negocioId, cancellationToken);
                if (previa is not { Tipo: TipoGestaoWhatsApp.Remarcar, ProfissionalId: { } profissionalDoResumo })
                {
                    await transacao.RollbackAsync(cancellationToken);
                    return;
                }

                // A pendência é da conversa deste telefone, nunca de outra (mesmo negócio ou não).
                var conversa = await _dbContext.ConversasWhatsApp.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == previa.ConversaId && c.NegocioId == negocioId, cancellationToken);
                if (conversa is null || !TelefoneE164.TentarCriar(conversa.TelefoneCliente, out var telefoneDaConversa)
                    || !telefone.Equals(telefoneDaConversa))
                {
                    await transacao.RollbackAsync(cancellationToken);
                    return;
                }

                await TravarAgendaDoProfissionalAsync(profissionalDoResumo, cancellationToken);

                var pendencia = await _dbContext.PendenciasGestaoWhatsApp.FirstAsync(p => p.Id == previa.Id, cancellationToken);
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
                    await transacao.RollbackAsync(cancellationToken);
                    return;
                }

                if (!pendencia.Aguardando)
                {
                    resultado = ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.Encerrada, pendencia.AgendamentoId);
                    await transacao.RollbackAsync(cancellationToken);
                    return;
                }

                async Task RecusarAsync(MotivoRecusaGestaoAgente motivo, bool expirar = false)
                {
                    resultado = await EncerrarERecusarGestaoAsync(pendencia, motivo, pendencia.AgendamentoId, expirar, null, cancellationToken);
                    await transacao.CommitAsync(cancellationToken);
                }

                var agendamento = await _dbContext.Agendamentos.Include(a => a.Servicos)
                    .FirstOrDefaultAsync(a => a.Id == pendencia.AgendamentoId && a.NegocioId == negocioId, cancellationToken);
                if (agendamento is null)
                {
                    await RecusarAsync(MotivoRecusaGestaoAgente.NaoPermitido);
                    return;
                }

                await TravarLinhaERelerAsync(agendamento, cancellationToken);

                // O instante vale DEPOIS de ter as travas e o estado relido: a espera pode atravessar o prazo.
                var agora = DateTimeOffset.UtcNow;
                if (pendencia.Vencida(agora))
                {
                    await RecusarAsync(MotivoRecusaGestaoAgente.Expirada, expirar: true);
                    return;
                }

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
                {
                    await RecusarAsync(MotivoRecusaGestaoAgente.NaoPermitido);
                    return;
                }

                agora = DateTimeOffset.UtcNow;
                if (pendencia.Vencida(agora))
                {
                    await RecusarAsync(MotivoRecusaGestaoAgente.Expirada, expirar: true);
                    return;
                }

                // As MESMAS regras do link público: só Agendado, e a antecedência mínima sobre o início ATUAL (conferida aqui, na transação).
                if (agendamento.Status != StatusAgendamento.Agendado || !agendamento.PodeSerMovido)
                {
                    await RecusarAsync(MotivoRecusaGestaoAgente.StatusNaoPermite);
                    return;
                }

                if (GestaoPublicaAgendamento.MotivoTemporal(agendamento.Inicio, agora, _opcoesAgendamentoPublico.AntecedenciaMinimaCancelamentoHoras) is not null)
                {
                    await RecusarAsync(MotivoRecusaGestaoAgente.AntecedenciaMinima);
                    return;
                }

                // O que o cliente viu no resumo (profissional, início atual, duração) continua valendo? Senão, outro caminho mexeu no agendamento.
                var duracao = agendamento.Servicos.Sum(s => s.DuracaoMinutos);
                if (agendamento.ProfissionalId != profissionalDoResumo || pendencia.InicioAnterior != agendamento.Inicio
                    || pendencia.DuracaoMinutos != duracao || pendencia.NovoInicio is not { } novoInicio || duracao <= 0)
                {
                    await RecusarAsync(MotivoRecusaGestaoAgente.AgendamentoAlterado);
                    return;
                }

                // Profissional que deixou de atender (inativo/excluído) não recebe horário novo.
                if (!await _dbContext.Profissionais.AnyAsync(
                        p => p.Id == agendamento.ProfissionalId && p.NegocioId == negocioId && p.Ativo && !p.Excluido, cancellationToken))
                {
                    await RecusarAsync(MotivoRecusaGestaoAgente.NaoPermitido);
                    return;
                }

                var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == negocioId, cancellationToken);
                var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);
                var novoFim = novoInicio.AddMinutes(duracao);
                var (diaLocal, _) = ConversorFusoHorario.ParaLocal(novoInicio, fuso);

                // O novo início precisa estar EXATAMENTE na oferta pública (o horário atual do próprio agendamento não conta como ocupado)
                // e fora de qualquer forçado, conferido AGORA, com a agenda do profissional travada.
                var livres = await _consultaDisponibilidade.ListarHorariosLivresAsync(
                    agendamento.ProfissionalId, diaLocal, duracao, cancellationToken, agendamento.Id);
                if (novoInicio <= agora || novoInicio == agendamento.Inicio || !livres.Contains(novoInicio)
                    || await SobrepoeForcadoAsync(agendamento.ProfissionalId, novoInicio, novoFim, agendamento.Id, cancellationToken))
                {
                    var proximos = livres.Where(h => h != agendamento.Inicio).Take(5).ToList();
                    resultado = await EncerrarERecusarGestaoAsync(
                        pendencia, MotivoRecusaGestaoAgente.HorarioIndisponivel, agendamento.Id, false, proximos, cancellationToken);
                    await transacao.CommitAsync(cancellationToken);
                    return;
                }

                var inicioAnterior = agendamento.Inicio;
                agendamento.Mover(novoInicio, novoFim, agora);

                // O lembrete do horário antigo deixa de valer; o do novo nasce da varredura. O profissional é avisado pelo caminho do mover normal.
                await _outbox.CancelarLembretesDeOutroHorarioAsync(agendamento.Id, TiposDeNotificacao.VersaoDoHorario(novoInicio), cancellationToken);
                if (usaOutbox)
                    await _outbox.EnfileirarAvisoAoProfissionalAsync(agendamento, EventoAgendamentoProfissional.Remarcado, cancellationToken);

                pendencia.Efetivar(agora, dados.MensagemConfirmacaoId, dados.OperacaoId);
                await RegistrarAuditoriaRemarcacaoPeloAgenteAsync(agendamento, pendencia.Id, inicioAnterior, negocio.Fuso, cancellationToken);

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transacao.CommitAsync(cancellationToken);
                }
                catch (DbUpdateException excecao) when (excecao.EhViolacaoDeExclusao())
                {
                    // Perdeu a corrida para outra gravação (a constraint é a garantia real): nada fica gravado, o pedido é encerrado fora
                    // da transação desfeita e o texto traz horários livres.
                    await transacao.RollbackAsync(cancellationToken);
                    _dbContext.ChangeTracker.Clear();
                    falharDepois = pendencia.Id;
                    profissionalDoConflito = profissionalDoResumo;
                    novoInicioDoConflito = novoInicio;
                    duracaoDoConflito = duracao;
                    resultado = ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.HorarioIndisponivel, pendencia.AgendamentoId);
                    return;
                }

                resultado = ResultadoGestaoAgente.Efetivada(agendamento.Id, true);
                agendamentoNotificar = agendamento.Id;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra transição da mesma pendência (assumir, expiração, preparo novo) venceu: vale o que ela gravou.
            _dbContext.ChangeTracker.Clear();
            return ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.Encerrada);
        }

        if (falharDepois is { } pendenciaId && profissionalDoConflito is { } profissionalId)
        {
            var agoraFalha = DateTimeOffset.UtcNow;
            await _dbContext.PendenciasGestaoWhatsApp
                .Where(p => p.Id == pendenciaId && p.NegocioId == negocioId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.Status, StatusPendenciaAgendamentoWhatsApp.Falhou)
                    .SetProperty(p => p.ConcluidaEm, agoraFalha), cancellationToken);

            var nomeFuso = await _dbContext.Negocios.AsNoTracking().Where(n => n.Id == negocioId).Select(n => n.Fuso).FirstAsync(cancellationToken);
            var (dia, _) = ConversorFusoHorario.ParaLocal(novoInicioDoConflito, TimeZoneInfo.FindSystemTimeZoneById(nomeFuso));
            var livres = await _consultaDisponibilidade.ListarHorariosLivresAsync(
                profissionalId, dia, duracaoDoConflito, cancellationToken, resultado.AgendamentoId);
            resultado = ResultadoGestaoAgente.Recusada(MotivoRecusaGestaoAgente.HorarioIndisponivel, resultado.AgendamentoId, livres.Take(5).ToList());
        }

        // Sem outbox, o aviso ao profissional sai agora, depois do commit.
        if (resultado.Sucesso && resultado.EfetivadaAgora && !usaOutbox && agendamentoNotificar is { } id)
            await NotificarProfissionalAsync(id, EventoAgendamentoProfissional.Remarcado, cancellationToken);

        return resultado;
    }

    /// <summary>Auditoria da remarcação feita pelo agente (autor "sistema"): sem nome nem telefone do cliente.</summary>
    private async Task RegistrarAuditoriaRemarcacaoPeloAgenteAsync(
        Agendamento agendamento, Guid pendenciaId, DateTimeOffset inicioAnterior, string fuso, CancellationToken cancellationToken)
    {
        var profissional = await _dbContext.Profissionais.AsNoTracking().FirstAsync(p => p.Id == agendamento.ProfissionalId, cancellationToken);
        var servicos = string.Join(", ", agendamento.Servicos.Select(s => s.Nome));

        _auditoria.Registrar(
            AcoesAuditoria.RemarcarPeloWhatsApp, "Agendamento", agendamento.Id,
            $"pendência {pendenciaId}; serviço {servicos}; profissional {profissional.Nome}; início {FormatacaoBrasil.DataHora(inicioAnterior, fuso)} -> {FormatacaoBrasil.DataHora(agendamento.Inicio, fuso)}");
    }

    /// <summary>Encerra a pendência de gestão (Falhou, ou Expirada), grava e devolve o resultado recusado; o commit é de quem chamou.</summary>
    private async Task<ResultadoGestaoAgente> EncerrarERecusarGestaoAsync(
        PendenciaGestaoWhatsApp pendencia, MotivoRecusaGestaoAgente motivo, Guid? agendamentoId, bool expirar,
        IReadOnlyList<DateTimeOffset>? proximos, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;
        if (expirar)
            pendencia.Expirar(agora);
        else
            pendencia.Falhar(agora);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ResultadoGestaoAgente.Recusada(motivo, agendamentoId, proximos);
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
