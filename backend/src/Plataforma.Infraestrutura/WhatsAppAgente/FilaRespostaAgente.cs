using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Fila em memória (limitada) de conversas a processar: só o <c>ConversaId</c> trafega. Escolhida no lugar do Hangfire porque
/// o storage do Hangfire busca por polling (latência de segundos para uma conversa) e não há retry automático desejado
/// (repetir = resposta duplicada). O que se perde num reinício é recuperado por <see cref="RecuperadorRespostasAgente"/>.
/// </summary>
public sealed class FilaRespostaAgenteEmMemoria : IFilaRespostaAgente
{
    public const int Capacidade = 1000;

    private readonly Channel<Guid> _canal = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacidade)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
    });

    private readonly ILogger<FilaRespostaAgenteEmMemoria> _logger;

    public FilaRespostaAgenteEmMemoria(ILogger<FilaRespostaAgenteEmMemoria> logger) => _logger = logger;

    public ChannelReader<Guid> Leitor => _canal.Reader;

    public Task<bool> EnfileirarAsync(Guid conversaId, CancellationToken cancellationToken = default)
    {
        if (_canal.Writer.TryWrite(conversaId))
            return Task.FromResult(true);

        // Cheia: descarta e loga só o ID; a varredura de recuperação pega depois.
        _logger.LogWarning("Agente: fila cheia; conversa {ConversaId} fica para a recuperação.", conversaId);
        return Task.FromResult(false);
    }
}

/// <summary>Varredura de recuperação: mensagens de usuário não tratadas (fila perdida num deploy/hibernação).</summary>
public sealed class RecuperadorRespostasAgente
{
    public static readonly TimeSpan IdadeMinima = TimeSpan.FromSeconds(30);

    private readonly PlataformaDbContext _dbContext;

    public RecuperadorRespostasAgente(PlataformaDbContext dbContext) => _dbContext = dbContext;

    /// <summary>Quanto tempo para trás a varredura procura anúncios de fatos efetivados que nunca saíram.</summary>
    public static readonly TimeSpan JanelaDosAnuncios = TimeSpan.FromDays(7);

    /// <summary>
    /// Conversas a retomar. Só IDs. Duas listas, separadas de propósito:
    /// (1) mensagem de usuário pendente criada há mais de 30 s, sem lease ativo e com agente ativo e fora de atendimento humano — a rodada
    /// descarta sem responder as que têm mais de 10 min (resposta atrasada confunde);
    /// (2) FATOS já efetivados e ainda não anunciados (agendamento confirmado ou cancelamento feito pelo agente, sem a mensagem do servidor
    /// depois do commit e com o lote do "sim" ainda não tratado), em QUALQUER estado da conversa (inclusive Humano) e de QUALQUER idade:
    /// o cliente tem que saber o que foi feito. Quem decide o que enviar é o processador.
    /// Única consulta, além do webhook, que ignora o filtro de negócio — por isso devolve apenas IDs.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ListarPendentesAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var limite = agora - IdadeMinima;
        var novas = await _dbContext.ConversasWhatsApp.IgnoreQueryFilters().AsNoTracking()
            .Where(c => (c.ProcessandoAte == null || c.ProcessandoAte < agora)
                && c.Status == StatusConversaWhatsApp.Agente
                && _dbContext.InstanciasWhatsApp.IgnoreQueryFilters().Any(i => i.Id == c.InstanciaId && i.AgenteAtivo)
                && _dbContext.MensagensWhatsApp.IgnoreQueryFilters().Any(m =>
                    m.ConversaId == c.Id && m.Papel == PapelMensagemWhatsApp.Usuario
                    && (c.ProcessadaAte == null || m.CriadoEm > c.ProcessadaAte) && m.CriadoEm < limite))
            .Select(c => c.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

        var desde = agora - JanelaDosAnuncios;

        // Cancelamento efetivado: pendência de gestão Confirmada, sem a mensagem gestao_efetivada depois do commit, com o lote do "sim" aberto.
        var cancelamentos = await (
            from p in _dbContext.PendenciasGestaoWhatsApp.IgnoreQueryFilters().AsNoTracking()
            join c in _dbContext.ConversasWhatsApp.IgnoreQueryFilters().AsNoTracking() on p.ConversaId equals c.Id
            join m in _dbContext.MensagensWhatsApp.IgnoreQueryFilters().AsNoTracking() on p.ConfirmacaoMensagemId equals (Guid?)m.Id
            where p.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada && p.ConcluidaEm >= desde
                && (c.ProcessandoAte == null || c.ProcessandoAte < agora)
                && (c.ProcessadaAte == null || c.ProcessadaAte < m.CriadoEm)
                && !c.TelefoneCliente.StartsWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado)
                && !_dbContext.MensagensWhatsApp.IgnoreQueryFilters().Any(x =>
                    x.ConversaId == c.Id && x.Papel == PapelMensagemWhatsApp.Sistema && x.NomeFerramenta == MarcadoresAgente.GestaoEfetivada
                    && x.CriadoEm >= p.ConcluidaEm)
            select c.Id).Take(200).ToListAsync(cancellationToken);

        // Agendamento confirmado: mesma ideia, com o marcador agendamento_confirmado.
        var confirmacoes = await (
            from p in _dbContext.PendenciasAgendamentoWhatsApp.IgnoreQueryFilters().AsNoTracking()
            join c in _dbContext.ConversasWhatsApp.IgnoreQueryFilters().AsNoTracking() on p.ConversaId equals c.Id
            join m in _dbContext.MensagensWhatsApp.IgnoreQueryFilters().AsNoTracking() on p.ConfirmacaoMensagemId equals (Guid?)m.Id
            where p.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada && p.ConcluidaEm >= desde
                && (c.ProcessandoAte == null || c.ProcessandoAte < agora)
                && (c.ProcessadaAte == null || c.ProcessadaAte < m.CriadoEm)
                && !c.TelefoneCliente.StartsWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado)
                && !_dbContext.MensagensWhatsApp.IgnoreQueryFilters().Any(x =>
                    x.ConversaId == c.Id && x.Papel == PapelMensagemWhatsApp.Sistema && x.NomeFerramenta == MarcadoresAgente.Confirmado
                    && x.CriadoEm >= p.ConcluidaEm)
            select c.Id).Take(200).ToListAsync(cancellationToken);

        return novas.Concat(cancelamentos).Concat(confirmacoes).Distinct().ToList();
    }
}

/// <summary>Consome a fila (até 4 rodadas em paralelo, conversas diferentes) e faz a varredura de recuperação a cada 60 s.</summary>
public sealed class ProcessadorFilaAgente : BackgroundService
{
    public const int Paralelismo = 4;
    private static readonly TimeSpan IntervaloRecuperacao = TimeSpan.FromSeconds(60);

    private readonly FilaRespostaAgenteEmMemoria _fila;
    private readonly IServiceScopeFactory _escopos;
    private readonly ILogger<ProcessadorFilaAgente> _logger;

    public ProcessadorFilaAgente(FilaRespostaAgenteEmMemoria fila, IServiceScopeFactory escopos, ILogger<ProcessadorFilaAgente> logger)
    {
        _fila = fila;
        _escopos = escopos;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tarefas = Enumerable.Range(0, Paralelismo).Select(_ => ConsumirAsync(stoppingToken)).ToList();
        tarefas.Add(RecuperarPeriodicamenteAsync(stoppingToken));
        return Task.WhenAll(tarefas);
    }

    private async Task ConsumirAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var conversaId in _fila.Leitor.ReadAllAsync(cancellationToken))
                await ProcessarAsync(conversaId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Encerrando.
        }
    }

    private async Task ProcessarAsync(Guid conversaId, CancellationToken cancellationToken)
    {
        try
        {
            using var escopo = _escopos.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<IProcessadorRodadaAgente>().ProcessarAsync(conversaId, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            _logger.LogWarning("Agente: falha ao processar a fila (conversa {ConversaId}): {Tipo}.", conversaId, excecao.GetType().Name);
        }
    }

    private async Task RecuperarPeriodicamenteAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var temporizador = new PeriodicTimer(IntervaloRecuperacao);
            do
            {
                await RecuperarAsync(cancellationToken);
            }
            while (await temporizador.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // Encerrando.
        }
    }

    public async Task RecuperarAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var escopo = _escopos.CreateScope();
            var pendentes = await escopo.ServiceProvider.GetRequiredService<RecuperadorRespostasAgente>()
                .ListarPendentesAsync(DateTimeOffset.UtcNow, cancellationToken);

            foreach (var id in pendentes)
                await _fila.EnfileirarAsync(id, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            // Banco indisponível (ex.: Neon acordando): tenta de novo no próximo ciclo.
            _logger.LogWarning("Agente: falha na varredura de recuperação: {Tipo}.", excecao.GetType().Name);
        }
    }
}
