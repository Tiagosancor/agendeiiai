using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Atendimento humano das conversas (Fase 4, G12/G13). Cada transição de estado é UM <c>UPDATE</c> condicional no banco: assumir só sai
/// de Agente, devolver só sai de Humano, e a rodada do agente confere <c>status = 'Agente'</c> no mesmo tipo de <c>UPDATE</c> (lease e
/// "processado") — por isso, quem assume no meio de uma resposta da IA garante que nada dela é enviado. Nenhum texto de conversa
/// passa por aqui (auditoria e logs só com ids).
/// </summary>
public sealed class GerenciadorConversasWhatsApp : IGerenciadorConversasWhatsApp
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IRegistroAuditoria _auditoria;
    private readonly GerenciadorPendenciasAgente _pendencias;
    private readonly OpcoesIa _opcoes;
    private readonly ILogger<GerenciadorConversasWhatsApp> _logger;

    public GerenciadorConversasWhatsApp(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IUsuarioAtual usuarioAtual, IRegistroAuditoria auditoria,
        GerenciadorPendenciasAgente pendencias, IOptions<OpcoesIa> opcoes, ILogger<GerenciadorConversasWhatsApp> logger)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _usuarioAtual = usuarioAtual;
        _auditoria = auditoria;
        _pendencias = pendencias;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public async Task<ResultadoConversaWhatsApp> AssumirAsync(Guid conversaId, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        if (!await ExisteAsync(negocioId, conversaId, cancellationToken))
            return ResultadoConversaWhatsApp.NaoEncontrada;

        var agora = DateTimeOffset.UtcNow;
        DateTimeOffset? ate = _opcoes.Humano.PrazoDevolucao is { } prazo ? agora + prazo : null;
        var usuarioId = _usuarioAtual.UsuarioId;

        var mudou = await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var linhas = await _dbContext.ConversasWhatsApp
                .Where(c => c.Id == conversaId && c.NegocioId == negocioId && c.Status == StatusConversaWhatsApp.Agente
                    && !c.TelefoneCliente.StartsWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, StatusConversaWhatsApp.Humano)
                    .SetProperty(c => c.HumanoDesde, (DateTimeOffset?)agora)
                    .SetProperty(c => c.HumanoAte, ate)
                    .SetProperty(c => c.HumanoMotivo, (MotivoHumanoConversa?)MotivoHumanoConversa.Painel)
                    .SetProperty(c => c.HumanoPorUsuarioId, usuarioId)
                    .SetProperty(c => c.EpocaAtendimento, c => c.EpocaAtendimento + 1), cancellationToken);

            // Já estava em Humano (por pedido do agente): quem clicou passa a constar como quem atende, se ainda ninguém constava.
            if (linhas == 0 && usuarioId is not null)
            {
                linhas = await _dbContext.ConversasWhatsApp
                    .Where(c => c.Id == conversaId && c.NegocioId == negocioId && c.Status == StatusConversaWhatsApp.Humano
                        && c.HumanoPorUsuarioId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.HumanoPorUsuarioId, usuarioId), cancellationToken);
            }

            if (linhas > 0)
            {
                _auditoria.Registrar(AcoesAuditoria.AssumirConversaWhatsApp, nameof(ConversaWhatsApp), conversaId);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transacao.CommitAsync(cancellationToken);
            return linhas > 0;
        });

        // Depois do commit (ordem de locks: pendência → agendamento): ou uma confirmação em andamento ganha a pendência e é anunciada,
        // ou esta rotina ganha e nada é efetivado. Nunca um agendamento confirmado sem anúncio.
        await EncerrarPendenciasAbertasAsync(conversaId, cancellationToken);

        if (mudou)
            _logger.LogInformation("Agente: conversa assumida pelo painel (conversa {ConversaId}, negócio {NegocioId}).", conversaId, negocioId);
        return ResultadoConversaWhatsApp.Ok;
    }

    public async Task<ResultadoConversaWhatsApp> DevolverAsync(Guid conversaId, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        if (!await ExisteAsync(negocioId, conversaId, cancellationToken))
            return ResultadoConversaWhatsApp.NaoEncontrada;

        var agora = DateTimeOffset.UtcNow;
        var mudou = await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var linhas = await DevolverAtomicoAsync(_dbContext, negocioId, conversaId, agora, somenteSePrazoVencido: false, cancellationToken);
            if (linhas > 0)
            {
                _auditoria.Registrar(AcoesAuditoria.DevolverConversaWhatsApp, nameof(ConversaWhatsApp), conversaId);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transacao.CommitAsync(cancellationToken);
            return linhas > 0;
        });

        if (mudou)
            _logger.LogInformation("Agente: conversa devolvida ao agente (conversa {ConversaId}, negócio {NegocioId}).", conversaId, negocioId);
        return ResultadoConversaWhatsApp.Ok;
    }

    /// <summary>
    /// Devolve ao agente num único <c>UPDATE</c>: limpa o estado humano, grava <c>devolvida_em</c> e leva <c>processada_ate</c> até a última
    /// mensagem do cliente JÁ GRAVADA — o acumulado nunca é respondido. Com <paramref name="somenteSePrazoVencido"/> (devolução
    /// preguiçosa do webhook) só devolve se <c>humano_ate</c> já passou. Devolve as linhas atingidas (0 = não estava em Humano).
    /// </summary>
    public static async Task<int> DevolverAtomicoAsync(
        PlataformaDbContext dbContext, Guid negocioId, Guid conversaId, DateTimeOffset agora, bool somenteSePrazoVencido, CancellationToken cancellationToken)
    {
        // Trava a linha em um statement SEPARADO: o UPDATE abaixo, em READ COMMITTED, calcula a subconsulta do "processado até" com o
        // snapshot do próprio statement; se esperasse o lock dentro dele, não veria a mensagem que o webhook acabou de commitar.
        // Roda na transação do chamador (webhook ou DevolverAsync).
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM conversas_whatsapp WHERE id = {conversaId} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);

        return await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE conversas_whatsapp c
               SET status = 'Agente', devolvida_em = {agora}, epoca_atendimento = c.epoca_atendimento + 1,
                   humano_desde = NULL, humano_ate = NULL, humano_motivo = NULL, humano_por_usuario_id = NULL,
                   processada_ate = GREATEST(c.processada_ate,
                       (SELECT max(m.criado_em) FROM mensagens_whatsapp m
                         WHERE m.conversa_id = c.id AND m.negocio_id = c.negocio_id AND m.papel = 'Usuario'))
             WHERE c.id = {conversaId} AND c.negocio_id = {negocioId} AND c.status = 'Humano'
               AND (NOT {somenteSePrazoVencido} OR (c.humano_ate IS NOT NULL AND c.humano_ate <= {agora}))
            """, cancellationToken);
    }

    public async Task<ResultadoMarcarHumano> MarcarHumanoPeloAgenteAsync(
        Guid conversaId, MotivoChamadaAtendente motivo, int? epocaEsperada = null, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var agora = DateTimeOffset.UtcNow;
        DateTimeOffset? ate = _opcoes.Humano.PrazoDevolucao is { } prazo ? agora + prazo : null;
        var inicioDoDia = new DateTimeOffset(agora.UtcDateTime.Date, TimeSpan.Zero);
        var corteDoAviso = agora.AddHours(-_opcoes.Humano.IntervaloAvisoPorConversaHoras);
        var chave = $"humano-aviso:{negocioId:N}:{agora.UtcDateTime:yyyyMMdd}";

        var resultado = await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            // O teto de avisos por negócio/dia é decidido sob uma trava só (contagem + marca juntas): conversas simultâneas nunca passam dele.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({chave}, 0))", cancellationToken);

            var linhas = await _dbContext.ConversasWhatsApp
                .Where(c => c.Id == conversaId && c.NegocioId == negocioId && c.Status == StatusConversaWhatsApp.Agente
                    && !c.TelefoneCliente.StartsWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado)
                    // Rodada de antes de um assumir/devolver não chama atendente (época mudou).
                    && (epocaEsperada == null || c.EpocaAtendimento == epocaEsperada)
                    // A contratação é conferida no próprio UPDATE: revogar o serviço no meio da rodada impede a transferência.
                    && _dbContext.Negocios.Any(n => n.Id == c.NegocioId && n.AgenteIaContratado))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, StatusConversaWhatsApp.Humano)
                    .SetProperty(c => c.HumanoDesde, (DateTimeOffset?)agora)
                    .SetProperty(c => c.HumanoAte, ate)
                    .SetProperty(c => c.HumanoMotivo, (MotivoHumanoConversa?)MotivoHumanoConversa.PedidoDoAgente)
                    .SetProperty(c => c.HumanoPorUsuarioId, (Guid?)null), cancellationToken);
            if (linhas == 0)
            {
                await transacao.RollbackAsync(cancellationToken);
                return new ResultadoMarcarHumano(false, false);
            }

            var avisar = false;
            // O teto conta AVISOS (uma linha por e-mail autorizado), não conversas: a mesma conversa avisada duas vezes no dia conta duas.
            var avisosHoje = await _dbContext.AvisosAtendenteWhatsApp.AsNoTracking()
                .CountAsync(a => a.NegocioId == negocioId && a.Em >= inicioDoDia, cancellationToken);
            if (avisosHoje < _opcoes.Humano.LimiteAvisosPorNegocioPorDia)
            {
                avisar = await _dbContext.ConversasWhatsApp
                    .Where(c => c.Id == conversaId && c.NegocioId == negocioId && (c.AtendenteAvisadoEm == null || c.AtendenteAvisadoEm < corteDoAviso))
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.AtendenteAvisadoEm, (DateTimeOffset?)agora), cancellationToken) == 1;
                if (avisar)
                    _dbContext.AvisosAtendenteWhatsApp.Add(new AvisoAtendenteWhatsApp(negocioId, conversaId, agora));
            }

            _auditoria.Registrar(AcoesAuditoria.AtenderConversaPeloAgente, nameof(ConversaWhatsApp), conversaId, $"motivo {motivo}");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return new ResultadoMarcarHumano(true, avisar);
        });

        if (resultado.Marcada)
        {
            // O que estava à espera de um "sim" não vale mais: o horário reservado é liberado.
            await EncerrarPendenciasAbertasAsync(conversaId, cancellationToken);
            _logger.LogInformation("Agente: atendente chamado (conversa {ConversaId}, negócio {NegocioId}, motivo {Motivo}, aviso {Aviso}).",
                conversaId, negocioId, motivo, resultado.AvisarAtendente);
        }

        return resultado;
    }

    private Task<bool> ExisteAsync(Guid negocioId, Guid conversaId, CancellationToken cancellationToken) =>
        _dbContext.ConversasWhatsApp.AsNoTracking().AnyAsync(
            c => c.Id == conversaId && c.NegocioId == negocioId && !c.TelefoneCliente.StartsWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado),
            cancellationToken);

    /// <summary>Encerra (<c>Falhou</c>, libera a reserva) as pendências de agendamento ainda abertas, cada uma sob a trava dela.</summary>
    private async Task EncerrarPendenciasAbertasAsync(Guid conversaId, CancellationToken cancellationToken)
    {
        var ids = await _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking()
            .Where(p => p.ConversaId == conversaId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando)
            .Select(p => p.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
            await _pendencias.EncerrarAsync(id, DesfechoPendenciaAgente.Falhou, cancellationToken);
    }
}
