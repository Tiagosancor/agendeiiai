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

    /// <summary>
    /// Conversas com mensagem de usuário pendente criada há mais de 30 s, sem lease ativo e com agente ativo. Só IDs.
    /// Mensagens com mais de 10 min entram também: a rodada as descarta sem responder (resposta atrasada confunde).
    /// Única consulta, além do webhook, que ignora o filtro de negócio — por isso devolve apenas IDs.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ListarPendentesAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var limite = agora - IdadeMinima;
        return await _dbContext.ConversasWhatsApp.IgnoreQueryFilters().AsNoTracking()
            .Where(c => (c.ProcessandoAte == null || c.ProcessandoAte < agora)
                && c.Status == StatusConversaWhatsApp.Agente
                && _dbContext.InstanciasWhatsApp.IgnoreQueryFilters().Any(i => i.Id == c.InstanciaId && i.AgenteAtivo)
                && _dbContext.MensagensWhatsApp.IgnoreQueryFilters().Any(m =>
                    m.ConversaId == c.Id && m.Papel == PapelMensagemWhatsApp.Usuario
                    && (c.ProcessadaAte == null || m.CriadoEm > c.ProcessadaAte) && m.CriadoEm < limite))
            .Select(c => c.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
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
