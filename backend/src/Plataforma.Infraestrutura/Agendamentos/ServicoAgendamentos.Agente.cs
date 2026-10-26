using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Agendamento pelo agente de WhatsApp (Fase 3). Dois métodos internos, sem controller: o modelo nunca chama nada daqui, e
/// nenhum recebe motivo de forçar (impossível por tipo). A garantia contra conflito continua sendo a exclusion constraint.
/// </summary>
public sealed partial class ServicoAgendamentos
{
    private const string AcaoAgendarPeloWhatsApp = "AgendarPeloWhatsApp";

    public async Task<ResultadoAgendamento> CriarReservaAgenteAsync(CriarReservaAgente dados, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;

        if (await _assinaturas.ObterAsync(negocioId, cancellationToken) is { PermiteOperar: false })
            return ResultadoAgendamento.ComErro("O agendamento online não está disponível agora.");

        // Tudo filtrado pelo tenant da conversa: id de outro negócio é "não encontrado".
        var profissional = await _dbContext.Profissionais.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dados.ProfissionalId, cancellationToken);
        if (profissional is not { Ativo: true, Excluido: false })
            return ResultadoAgendamento.ComErro("Este profissional não atende mais.");

        var servico = await _dbContext.Servicos.AsNoTracking().FirstOrDefaultAsync(s => s.Id == dados.ServicoId, cancellationToken);
        if (servico is not { Ativo: true, Excluido: false })
            return ResultadoAgendamento.ComErro("Este serviço não está disponível.");

        if (!await _dbContext.ProfissionalServicos.AnyAsync(
                v => v.ProfissionalId == dados.ProfissionalId && v.ServicoId == dados.ServicoId, cancellationToken))
            return ResultadoAgendamento.ComErro("Este profissional não faz esse serviço.");

        var (itens, erroServicos) = await MontarItensAsync(dados.ProfissionalId, [dados.ServicoId], cancellationToken);
        if (erroServicos is not null)
            return ResultadoAgendamento.ComErro(erroServicos);

        // O horário precisa estar exatamente na oferta pública (grade de 15 min, expediente, almoço, folga, bloqueio, passado,
        // ocupado e forçado). Comparação por instante. A checagem é só a primeira barreira: quem decide a corrida é a trava
        // por profissional + a exclusion constraint dentro de CriarInternoAsync.
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == negocioId, cancellationToken);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);
        var duracao = itens.Sum(i => i.DuracaoMinutos);
        var (diaLocal, _) = ConversorFusoHorario.ParaLocal(dados.Inicio, fuso);

        var livres = await _consultaDisponibilidade.ListarHorariosLivresAsync(dados.ProfissionalId, diaLocal, duracao, cancellationToken);
        if (!livres.Any(h => h == dados.Inicio))
            return ResultadoAgendamento.ComConflito("Esse horário não está disponível. Escolha outro.", livres.Take(5).ToList());

        return await CriarInternoAsync(
            new CriarAgendamento(dados.ProfissionalId, null, [dados.ServicoId], dados.Inicio),
            confirmarDeImediato: false, cancellationToken,
            new OpcoesCriacao(OrigemAgendamento.WhatsApp, IniciarAtendimento: false, ClienteAutorizouMensagens: false));
    }

    public async Task<ResultadoConfirmacaoAgente> ConfirmarReservaAgenteAsync(
        ConfirmarReservaAgente dados, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;

        if (!TelefoneE164.TentarCriar(dados.TelefoneConversa, out var telefone) || telefone is null)
            return ResultadoConfirmacaoAgente.Recusada(MotivoRecusaConfirmacaoAgente.NaoEncontrada);

        // Assinatura suspensa: nada é gravado e o agente fica em silêncio (a reserva expira sozinha). Lida fora da transação.
        if (await _assinaturas.ObterAsync(negocioId, cancellationToken) is { PermiteOperar: false })
            return ResultadoConfirmacaoAgente.Recusada(MotivoRecusaConfirmacaoAgente.AssinaturaSuspensa);

        var usaOutbox = _outbox.Usa(FamiliaNotificacao.Operacional);
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        var tentativas = new TentativasDaTransacao(_dbContext);
        var momento = NovoMomentoDaRequisicao();
        Guid? agendamentoNotificar = null;

        ResultadoConfirmacaoAgente resultado;
        try
        {
            resultado = await estrategia.ExecuteAsync(async () =>
            {
                agendamentoNotificar = null;
                tentativas.Iniciar();
                await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                var pendencia = await _dbContext.PendenciasAgendamentoWhatsApp
                    .FirstOrDefaultAsync(p => p.Id == dados.PendenciaId, cancellationToken);
                if (pendencia is null)
                {
                    await transacao.RollbackAsync(cancellationToken);
                    return ResultadoConfirmacaoAgente.Recusada(MotivoRecusaConfirmacaoAgente.NaoEncontrada);
                }

                // A pendência é da conversa deste telefone, nunca de outra (mesmo negócio ou não).
                var conversa = await _dbContext.ConversasWhatsApp.FirstOrDefaultAsync(c => c.Id == pendencia.ConversaId, cancellationToken);
                if (conversa is null || !TelefoneE164.TentarCriar(conversa.TelefoneCliente, out var telefoneDaConversa)
                    || !telefone.Equals(telefoneDaConversa))
                {
                    await transacao.RollbackAsync(cancellationToken);
                    return ResultadoConfirmacaoAgente.Recusada(MotivoRecusaConfirmacaoAgente.NaoEncontrada);
                }

                // Trava a pendência e relê: duas rodadas ou dois "sim" decidem sobre o estado atual, em fila.
                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM pendencias_agendamento_whatsapp WHERE id = {pendencia.Id} FOR UPDATE", cancellationToken);
                await _dbContext.Entry(pendencia).ReloadAsync(cancellationToken);

                if (pendencia.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada)
                {
                    // Repetição: o webhook do "sim" de novo, ou um segundo "sim". Só a tentativa desta mesma requisição, depois
                    // de um COMMIT ambíguo, é "quem confirmou agora" (e deve mandar a confirmação).
                    await transacao.RollbackAsync(cancellationToken);
                    var fuiEu = tentativas.Repetida && pendencia.ConfirmacaoMensagemId == dados.MensagemConfirmacaoId;
                    if (fuiEu)
                        agendamentoNotificar = pendencia.AgendamentoId;
                    return ResultadoConfirmacaoAgente.Confirmada(pendencia.AgendamentoId, fuiEu);
                }

                if (!pendencia.Aguardando)
                {
                    await transacao.RollbackAsync(cancellationToken);
                    return ResultadoConfirmacaoAgente.Recusada(MotivoRecusaConfirmacaoAgente.Encerrada);
                }

                var agora = DateTimeOffset.UtcNow;
                var agendamento = await _dbContext.Agendamentos
                    .Include(a => a.Servicos)
                    .FirstOrDefaultAsync(a => a.Id == pendencia.AgendamentoId, cancellationToken);
                if (agendamento is not null)
                    await TravarLinhaERelerAsync(agendamento, cancellationToken);

                // Prazo da pendência ou da reserva vencido, reserva cancelada ou já tomada: nunca confirma, e a reserva é liberada.
                if (agendamento is null || pendencia.Vencida(agora) || agendamento.Status != StatusAgendamento.Reservado
                    || agendamento.ReservadoAte < agora || agendamento.ClienteId is not null
                    || agendamento.Origem != OrigemAgendamento.WhatsApp || agendamento.NegocioId != negocioId)
                {
                    pendencia.Expirar(momento);
                    agendamento?.Expirar();
                    await SalvarEEncerrarAsync(transacao, cancellationToken);
                    return ResultadoConfirmacaoAgente.Recusada(MotivoRecusaConfirmacaoAgente.Expirada);
                }

                // Revalidação (E10): o que pode ter mudado entre o preparo e o "sim".
                if (!await AindaAgendavelAsync(agendamento, agora, cancellationToken))
                {
                    pendencia.Falhar(momento);
                    agendamento.Expirar();
                    await SalvarEEncerrarAsync(transacao, cancellationToken);
                    return ResultadoConfirmacaoAgente.Recusada(MotivoRecusaConfirmacaoAgente.IndisponivelAgora);
                }

                // Cliente pelo telefone da conversa (as duas formas do 9º dígito), sem nunca mexer em cadastro existente.
                var nome = string.IsNullOrWhiteSpace(dados.NomeInformado) ? pendencia.NomeInformado : dados.NomeInformado;
                var clienteId = await IdentificarOuCriarClienteDoAgenteAsync(negocioId, telefone, nome, cancellationToken);
                if (clienteId is null)
                {
                    await transacao.RollbackAsync(cancellationToken);
                    return ResultadoConfirmacaoAgente.Recusada(MotivoRecusaConfirmacaoAgente.NomeNecessario);
                }

                agendamento.VincularCliente(clienteId.Value);
                agendamento.RegistrarConsentimentoWhatsApp(momento, VersaoTermos.Atual);
                agendamento.ConfirmarReserva();
                pendencia.Confirmar(momento, dados.MensagemConfirmacaoId, VersaoTermos.Atual);
                conversa.VincularCliente(clienteId.Value);

                if (usaOutbox)
                {
                    await _outbox.EnfileirarConfirmacaoAsync(agendamento, cancellationToken);
                    await _outbox.EnfileirarAvisoAoProfissionalAsync(agendamento, EventoAgendamentoProfissional.Novo, cancellationToken);
                }

                await RegistrarAuditoriaAsync(agendamento, pendencia.Id, cancellationToken);

                var gravacao = await GravarConfirmacaoAsync(agendamento, transacao, cancellationToken);
                if (gravacao.Sucesso)
                {
                    agendamentoNotificar = agendamento.Id;
                    return ResultadoConfirmacaoAgente.Confirmada(agendamento.Id, true);
                }

                return ResultadoConfirmacaoAgente.Recusada(MotivoRecusaConfirmacaoAgente.Conflito, gravacao.ProximosHorariosLivres);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra transição da mesma pendência venceu (preparo novo, expiração): vale o que ela gravou.
            _dbContext.ChangeTracker.Clear();
            return ResultadoConfirmacaoAgente.Recusada(MotivoRecusaConfirmacaoAgente.Encerrada);
        }

        // Sem outbox, o aviso ao profissional (e o e-mail do cliente, se houver) saem agora, depois do commit. O texto da
        // confirmação ao cliente é da conversa, enviado por quem chamou.
        if (resultado.Sucesso && resultado.ConfirmadaAgora && !usaOutbox && agendamentoNotificar is { } id)
            await NotificarConfirmacaoAsync(id, cancellationToken);

        return resultado;
    }

    private async Task SalvarEEncerrarAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transacao, CancellationToken cancellationToken)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
    }

    /// <summary>Profissional ativo, serviços ativos e feitos por ele, sem bloqueio novo e ainda no futuro.</summary>
    private async Task<bool> AindaAgendavelAsync(Agendamento agendamento, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        if (agendamento.Inicio <= agora)
            return false;

        if (!await _dbContext.Profissionais.AnyAsync(
                p => p.Id == agendamento.ProfissionalId && p.Ativo && !p.Excluido, cancellationToken))
            return false;

        foreach (var linha in agendamento.Servicos)
        {
            var servicoId = linha.ServicoId;
            if (!await _dbContext.Servicos.AnyAsync(s => s.Id == servicoId && s.Ativo && !s.Excluido, cancellationToken))
                return false;
            if (!await _dbContext.ProfissionalServicos.AnyAsync(
                    v => v.ProfissionalId == agendamento.ProfissionalId && v.ServicoId == servicoId, cancellationToken))
                return false;
        }

        var inicio = agendamento.Inicio;
        var fim = agendamento.Fim;
        return !await _dbContext.BloqueiosAgenda.AnyAsync(
            b => b.ProfissionalId == agendamento.ProfissionalId && b.InicioUtc < fim && b.FimUtc > inicio, cancellationToken);
    }

    /// <summary>
    /// Procura o cliente pelas duas formas do 9º dígito (a de 13 dígitos primeiro) e usa o que existir, sem alterar nada do
    /// cadastro. Se não existe, cria na forma preferida com origem WhatsApp; nulo se faltou o nome. Corrida de duas
    /// confirmações com o mesmo telefone: <c>EhViolacaoDeUnicidade</c> e nova busca.
    /// </summary>
    private async Task<Guid?> IdentificarOuCriarClienteDoAgenteAsync(
        Guid negocioId, TelefoneE164 telefone, string? nome, CancellationToken cancellationToken)
    {
        var formas = telefone.FormasEquivalentes();

        var existente = await BuscarClientePorFormasAsync(negocioId, formas, cancellationToken);
        if (existente is not null)
            return existente.Id;

        var nomeLimpo = nome?.Trim();
        if (string.IsNullOrEmpty(nomeLimpo))
            return null;

        var novo = Cliente.Criar(negocioId, nomeLimpo, formas[0], OrigemCliente.WhatsApp);
        _dbContext.Clientes.Add(novo);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return novo.Id;
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            _dbContext.Entry(novo).State = EntityState.Detached;
            var daCorrida = await BuscarClientePorFormasAsync(negocioId, formas, cancellationToken);
            return daCorrida?.Id ?? throw excecao;
        }
    }

    private async Task<Cliente?> BuscarClientePorFormasAsync(
        Guid negocioId, IReadOnlyList<TelefoneE164> formas, CancellationToken cancellationToken)
    {
        foreach (var forma in formas)
        {
            var cliente = await _dbContext.Clientes.FirstOrDefaultAsync(
                c => c.NegocioId == negocioId && c.Telefone == forma, cancellationToken);
            if (cliente is not null)
                return cliente;
        }

        return null;
    }

    /// <summary>Auditoria do agendamento feito pelo agente (autor "sistema"): sem nome nem telefone do cliente.</summary>
    private async Task RegistrarAuditoriaAsync(Agendamento agendamento, Guid pendenciaId, CancellationToken cancellationToken)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == agendamento.NegocioId, cancellationToken);
        var profissional = await _dbContext.Profissionais.AsNoTracking().FirstAsync(p => p.Id == agendamento.ProfissionalId, cancellationToken);
        var servicos = string.Join(", ", agendamento.Servicos.Select(s => s.Nome));

        _auditoria.Registrar(
            AcaoAgendarPeloWhatsApp, "Agendamento", agendamento.Id,
            $"pendência {pendenciaId}; serviço {servicos}; profissional {profissional.Nome}; início {FormatacaoBrasil.DataHora(agendamento.Inicio, negocio.Fuso)}");
    }
}
