using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.Profissionais;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Notificacoes.Outbox;

/// <summary>Uma mensagem a gravar na outbox: só identificadores e metadados — o conteúdo e o destinatário nascem no despacho.</summary>
public sealed record NovaNotificacao(
    TipoNotificacao Tipo, CanalNotificacao Canal, string Chave, DateTimeOffset ExpiraEm,
    Guid? AgendamentoId = null, Guid? ClienteId = null, Guid? ProfissionalId = null, long? VersaoHorario = null);

/// <summary>
/// Gravação (e invalidação) das mensagens da outbox. Tudo roda na transação aberta do efeito de negócio — o comando e as suas mensagens
/// confirmam ou desfazem juntos — e a rede nunca é chamada aqui. A inserção é <c>INSERT … ON CONFLICT DO NOTHING</c> (como a
/// <c>Idempotency-Key</c>): repetir o comando, ou repetir a tentativa depois de um COMMIT ambíguo, reencontra a mesma chave e não cria
/// outra mensagem nem aborta a transação com um 23505.
/// </summary>
public sealed class OutboxNotificacoes
{
    private readonly PlataformaDbContext _dbContext;
    private readonly OpcoesNotificacoes _opcoes;
    private readonly TimeProvider _relogio;

    public OutboxNotificacoes(PlataformaDbContext dbContext, IOptions<OpcoesNotificacoes> opcoes, TimeProvider relogio)
    {
        _dbContext = dbContext;
        _opcoes = opcoes.Value;
        _relogio = relogio;
    }

    /// <summary>Esta família grava na outbox em vez de enviar direto? Decidido uma vez por operação, nunca pelos dois caminhos.</summary>
    public bool Usa(FamiliaNotificacao familia) => _opcoes.UsaOutbox(familia);

    // ------------------------------------------------------------------ canais

    /// <summary>E-mail se o cliente tem; WhatsApp se o negócio ativou as mensagens ao cliente e há telefone (mesma regra do envio direto).</summary>
    public static IReadOnlyList<CanalNotificacao> CanaisDoCliente(Negocio negocio, Cliente cliente)
    {
        var canais = new List<CanalNotificacao>(2);
        if (!string.IsNullOrWhiteSpace(cliente.Email))
            canais.Add(CanalNotificacao.Email);
        if (negocio.WhatsAppAtivoParaConfirmacoes && cliente.Telefone is not null)
            canais.Add(CanalNotificacao.WhatsApp);
        return canais;
    }

    /// <summary>E-mail se o profissional tem; WhatsApp só com o aviso ligado no negócio e telefone válido.</summary>
    public static IReadOnlyList<CanalNotificacao> CanaisDoProfissional(Negocio negocio, Profissional profissional)
    {
        var canais = new List<CanalNotificacao>(2);
        if (!string.IsNullOrWhiteSpace(profissional.Email))
            canais.Add(CanalNotificacao.Email);
        if (negocio.WhatsAppAvisoProfissional && Plataforma.Dominio.Comum.TelefoneE164.TentarCriar(profissional.Telefone ?? "", out _))
            canais.Add(CanalNotificacao.WhatsApp);
        return canais;
    }

    // ------------------------------------------------------------------ gravação

    /// <summary>Confirmação do agendamento ao cliente (um item por canal disponível).</summary>
    public async Task EnfileirarConfirmacaoAsync(Agendamento agendamento, CancellationToken cancellationToken)
    {
        if (agendamento.ClienteId is not { } clienteId)
            return;

        var cliente = await _dbContext.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, cancellationToken);
        if (cliente is null)
            return;

        var negocio = await ObterNegocioAsync(agendamento.NegocioId, cancellationToken);
        var expira = _opcoes.PrazoDeCriacao(FamiliaNotificacao.Operacional, _relogio.GetUtcNow());

        await EnfileirarAsync(agendamento.NegocioId, CanaisDoCliente(negocio, cliente).Select(canal => new NovaNotificacao(
            TipoNotificacao.Confirmacao, canal, TiposDeNotificacao.ChaveConfirmacao(agendamento.Id), expira,
            agendamento.Id, clienteId, null, TiposDeNotificacao.VersaoDoHorario(agendamento.Inicio))), cancellationToken);
    }

    /// <summary>Cancelamento pelo negócio, avisado ao cliente.</summary>
    public async Task EnfileirarCancelamentoDoClienteAsync(Agendamento agendamento, CancellationToken cancellationToken)
    {
        // Reserva que ainda não chegou a ter cliente (assistente público no meio): não há a quem avisar.
        if (agendamento.ClienteId is not { } clienteId)
            return;

        var cliente = await _dbContext.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, cancellationToken);
        if (cliente is null)
            return;

        var negocio = await ObterNegocioAsync(agendamento.NegocioId, cancellationToken);
        var expira = _opcoes.PrazoDeCriacao(FamiliaNotificacao.Operacional, _relogio.GetUtcNow());

        await EnfileirarAsync(agendamento.NegocioId, CanaisDoCliente(negocio, cliente).Select(canal => new NovaNotificacao(
            TipoNotificacao.Cancelamento, canal, TiposDeNotificacao.ChaveCancelamento(agendamento.Id), expira,
            agendamento.Id, clienteId, null, TiposDeNotificacao.VersaoDoHorario(agendamento.Inicio))), cancellationToken);
    }

    /// <summary>
    /// Aviso ao profissional do agendamento (novo, remarcado ou cancelado). O profissional da mensagem é o do agendamento no momento
    /// da gravação — numa transferência, o de destino. Remarcado leva a versão do horário novo.
    /// </summary>
    public async Task EnfileirarAvisoAoProfissionalAsync(
        Agendamento agendamento, EventoAgendamentoProfissional evento, CancellationToken cancellationToken)
    {
        var profissional = await _dbContext.Profissionais.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == agendamento.ProfissionalId, cancellationToken);
        if (profissional is null)
            return;

        var negocio = await ObterNegocioAsync(agendamento.NegocioId, cancellationToken);
        var versao = TiposDeNotificacao.VersaoDoHorario(agendamento.Inicio);
        var expira = _opcoes.PrazoDeCriacao(FamiliaNotificacao.Operacional, _relogio.GetUtcNow());

        var (tipo, chave) = evento switch
        {
            EventoAgendamentoProfissional.Novo => (TipoNotificacao.ProfissionalNovo, TiposDeNotificacao.ChaveProfissionalNovo(agendamento.Id, profissional.Id)),
            EventoAgendamentoProfissional.Remarcado => (TipoNotificacao.ProfissionalRemarcado, TiposDeNotificacao.ChaveProfissionalRemarcado(agendamento.Id, versao)),
            _ => (TipoNotificacao.ProfissionalCancelado, TiposDeNotificacao.ChaveProfissionalCancelado(agendamento.Id)),
        };

        await EnfileirarAsync(agendamento.NegocioId, CanaisDoProfissional(negocio, profissional).Select(canal => new NovaNotificacao(
            tipo, canal, chave, expira, agendamento.Id, agendamento.ClienteId, profissional.Id, versao)), cancellationToken);
    }

    /// <summary>
    /// Lembrete de 60 min de um agendamento (um item por canal). Vence <paramref name="naoEnviarFaltando"/> antes do atendimento: depois
    /// disso o despachante o cancela em vez de enviar. A chave leva a versão do horário — remarcar muda a chave.
    /// </summary>
    public async Task EnfileirarLembreteAsync(
        Agendamento agendamento, Cliente cliente, Negocio negocio, TimeSpan naoEnviarFaltando, CancellationToken cancellationToken)
    {
        var versao = TiposDeNotificacao.VersaoDoHorario(agendamento.Inicio);
        var expira = agendamento.Inicio - naoEnviarFaltando;

        await EnfileirarAsync(agendamento.NegocioId, CanaisDoCliente(negocio, cliente).Select(canal => new NovaNotificacao(
            TipoNotificacao.Lembrete60, canal, TiposDeNotificacao.ChaveLembrete60(agendamento.Id, versao), expira,
            agendamento.Id, cliente.Id, null, versao)), cancellationToken);
    }

    /// <summary>Lembrete de retorno (só WhatsApp). <paramref name="agendamentoBaseId"/> = o atendimento concluído de referência.</summary>
    public Task EnfileirarRetornoAsync(Guid negocioId, Guid clienteId, Guid agendamentoBaseId, CancellationToken cancellationToken) =>
        EnfileirarAsync(negocioId,
        [
            new NovaNotificacao(
                TipoNotificacao.LembreteRetorno, CanalNotificacao.WhatsApp, TiposDeNotificacao.ChaveRetorno(clienteId, agendamentoBaseId),
                _opcoes.PrazoDeCriacao(FamiliaNotificacao.Retorno, _relogio.GetUtcNow()), agendamentoBaseId, clienteId),
        ], cancellationToken);

    /// <summary>Insere os itens, ignorando os que já existem (mesma chave e canal no mesmo negócio). Devolve quantos foram criados.</summary>
    public async Task<int> EnfileirarAsync(Guid negocioId, IEnumerable<NovaNotificacao> itens, CancellationToken cancellationToken)
    {
        var criadas = 0;
        var agora = _relogio.GetUtcNow().ToUniversalTime();

        foreach (var item in itens)
        {
            var tipo = item.Tipo.ToString();
            var canal = item.Canal.ToString();
            var expiraEm = item.ExpiraEm.ToUniversalTime();

            criadas += await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO notificacoes_pendentes
                    (id, negocio_id, tipo, canal, chave_deduplicacao, status, tentativas, proxima_tentativa_em, expira_em,
                     agendamento_id, cliente_id, profissional_id, versao_horario, criado_em, atualizado_em)
                VALUES
                    ({Guid.NewGuid()}, {negocioId}, {tipo}, {canal}, {item.Chave}, 'Pendente', 0, {agora}, {expiraEm},
                     {item.AgendamentoId}::uuid, {item.ClienteId}::uuid, {item.ProfissionalId}::uuid, {item.VersaoHorario}::bigint, {agora}, {agora})
                ON CONFLICT (negocio_id, chave_deduplicacao, canal) DO NOTHING", cancellationToken);
        }

        return criadas;
    }

    // ------------------------------------------------------------------ invalidação

    /// <summary>
    /// O agendamento mudou de horário: o lembrete que ainda espera com o horário antigo deixa de valer. (Um item já reivindicado confere a
    /// versão de novo antes de chamar o provedor.) Devolve quantos itens foram cancelados.
    /// </summary>
    public Task<int> CancelarLembretesDeOutroHorarioAsync(Guid agendamentoId, long versaoAtual, CancellationToken cancellationToken)
    {
        var agora = _relogio.GetUtcNow().ToUniversalTime();
        var tipo = TipoNotificacao.Lembrete60.ToString();

        return _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE notificacoes_pendentes
               SET status = 'Cancelada', cancelada_em = {agora}, atualizado_em = {agora}, ultimo_erro_codigo = {CodigosOutbox.HorarioAlterado}
             WHERE agendamento_id = {agendamentoId} AND tipo = {tipo} AND status = 'Pendente'
               AND versao_horario IS DISTINCT FROM {versaoAtual}", cancellationToken);
    }

    /// <summary>
    /// O agendamento foi cancelado: confirmação, lembrete e avisos de "novo" ou "remarcado" ainda pendentes ficam obsoletos. (Os
    /// avisos de cancelamento, ao cliente e ao profissional, são os que precisam sair.)
    /// </summary>
    public Task<int> CancelarPendentesObsoletosDoCancelamentoAsync(Guid agendamentoId, CancellationToken cancellationToken)
    {
        var agora = _relogio.GetUtcNow().ToUniversalTime();
        string[] tipos =
        [
            TipoNotificacao.Confirmacao.ToString(), TipoNotificacao.Lembrete60.ToString(),
            TipoNotificacao.ProfissionalNovo.ToString(), TipoNotificacao.ProfissionalRemarcado.ToString(),
        ];

        return _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE notificacoes_pendentes
               SET status = 'Cancelada', cancelada_em = {agora}, atualizado_em = {agora}, ultimo_erro_codigo = {CodigosOutbox.AgendamentoObsoleto}
             WHERE agendamento_id = {agendamentoId} AND tipo = ANY({tipos}) AND status = 'Pendente'", cancellationToken);
    }

    private Task<Negocio> ObterNegocioAsync(Guid negocioId, CancellationToken cancellationToken) =>
        _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == negocioId, cancellationToken);
}
