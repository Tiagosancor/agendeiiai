using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Notificacoes.Outbox;

/// <summary>
/// Despachante da outbox (a cada minuto). Uma execução faz, nesta ordem:
/// <list type="number">
/// <item><b>Recupera</b> reivindicações de lease vencido. Sem "ponto sem retorno" gravado = a queda foi antes da chamada, o item volta a
/// <c>Pendente</c> sem gastar tentativa. Com ele, o provedor pode ter aceitado: WhatsApp vira <c>Desconhecida</c> (nunca reenvia
/// sozinho); e-mail volta pela política de tentativas (o preço é, no máximo, um e-mail repetido numa queda rara — nunca uma perda).</item>
/// <item><b>Vence</b> o que passou do prazo: lembretes são cancelados; operacionais viram falha definitiva.</item>
/// <item><b>Reivindica</b> um lote com <c>FOR UPDATE SKIP LOCKED</c> numa transação curta (marca <c>Enviando</c>, lease e token) e
/// confirma ANTES de qualquer chamada de rede; várias instâncias levam lotes diferentes.</item>
/// <item><b>Despacha</b> cada item fora de qualquer transação, por negócio, num escopo de DI próprio (<see cref="ProcessadorNotificacoesDoNegocio"/>).</item>
/// </list>
/// Política do Hangfire como a dos outros jobs: sem retry automático (o próximo minuto é a nova tentativa) e uma execução por vez
/// (<c>DisableConcurrentExecution</c>); mesmo sem ela, a reivindicação atômica e o token de cada gravação impedem envio duplicado.
/// </summary>
public sealed class JobDespacharNotificacoes
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IServiceScopeFactory _escopos;
    private readonly OpcoesNotificacoes _opcoes;
    private readonly TimeProvider _relogio;
    private readonly PoliticaDeTentativas _politica;
    private readonly ILogger<JobDespacharNotificacoes> _logger;

    public JobDespacharNotificacoes(
        PlataformaDbContext dbContext, IServiceScopeFactory escopos, IOptions<OpcoesNotificacoes> opcoes,
        TimeProvider relogio, ILogger<JobDespacharNotificacoes> logger)
    {
        _dbContext = dbContext;
        _escopos = escopos;
        _opcoes = opcoes.Value;
        _relogio = relogio;
        _politica = new PoliticaDeTentativas(_opcoes);
        _logger = logger;
    }

    /// <summary>Entrada do Hangfire.</summary>
    [DisableConcurrentExecution(timeoutInSeconds: 10)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecutarAsync(CancellationToken cancellationToken = default)
    {
        // Chave geral desligada = pausa operacional: nada é enviado, nada é descartado (o que está na tabela espera).
        if (!_opcoes.OutboxHabilitada)
            return;

        await ExecutarCicloAsync(cancellationToken);
    }

    /// <summary>Um ciclo completo (usado também pelos testes, que não passam pela chave geral do Hangfire).</summary>
    public async Task<int> ExecutarCicloAsync(CancellationToken cancellationToken = default)
    {
        var inicio = _relogio.GetUtcNow();
        await RecuperarLeasesVencidosAsync(cancellationToken);
        await RecuperarRetornosPresosAsync(cancellationToken);
        await VencerPrazosAsync(cancellationToken);

        var despachados = 0;
        while (!cancellationToken.IsCancellationRequested
            && _relogio.GetUtcNow() - inicio < TimeSpan.FromSeconds(_opcoes.DuracaoMaximaExecucaoSegundos))
        {
            var lote = await ReivindicarLoteAsync(cancellationToken);
            if (lote.Count == 0)
                break;

            foreach (var grupo in lote.GroupBy(i => i.NegocioId).OrderBy(g => g.Key))
            {
                try
                {
                    using var escopo = _escopos.CreateScope();
                    await escopo.ServiceProvider.GetRequiredService<ProcessadorNotificacoesDoNegocio>()
                        .ProcessarAsync(grupo.Key, grupo.ToList(), cancellationToken);
                    despachados += grupo.Count();
                }
                catch (Exception excecao) when (excecao is not OperationCanceledException)
                {
                    // Os itens que não foram tratados ficam com o lease: vencido, voltam pela recuperação.
                    _logger.LogError("Falha ao despachar as notificações do negócio {NegocioId} ({TipoErro}).", grupo.Key, excecao.GetType().Name);
                }
            }
        }

        return despachados;
    }

    // ------------------------------------------------------------------ reivindicação

    /// <summary>
    /// Transação curta: <c>SELECT … FOR UPDATE SKIP LOCKED</c> dos itens prontos, <c>Enviando</c> + lease + token, COMMIT. A rede só é
    /// chamada depois do commit — nenhuma trava nem transação aberta durante o envio.
    /// </summary>
    public async Task<IReadOnlyList<ItemReivindicado>> ReivindicarLoteAsync(CancellationToken cancellationToken)
    {
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var agora = _relogio.GetUtcNow().ToUniversalTime();
            var leaseAte = agora.AddSeconds(_opcoes.LeaseSegundos);
            var limite = _opcoes.TamanhoLote;

            var itens = await _dbContext.NotificacoesPendentes.FromSql($@"
                    SELECT * FROM notificacoes_pendentes
                     WHERE status = 'Pendente' AND proxima_tentativa_em <= {agora} AND expira_em > {agora}
                     ORDER BY proxima_tentativa_em, id
                     LIMIT {limite}
                       FOR UPDATE SKIP LOCKED")
                .IgnoreQueryFilters()
                .AsTracking()
                .ToListAsync(cancellationToken);

            var reivindicados = new List<ItemReivindicado>(itens.Count);
            foreach (var item in itens)
            {
                var token = Guid.NewGuid();
                item.Reivindicar(leaseAte, token);
                reivindicados.Add(new ItemReivindicado(item.Id, item.NegocioId, token));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            return reivindicados;
        });
    }

    // ------------------------------------------------------------------ recuperação e prazo

    public async Task RecuperarLeasesVencidosAsync(CancellationToken cancellationToken)
    {
        var agora = _relogio.GetUtcNow().ToUniversalTime();

        // Queda antes da chamada ao provedor: nada foi enviado. Volta como estava (a tentativa nem chegou a ser contada).
        var devolvidos = await _dbContext.NotificacoesPendentes.IgnoreQueryFilters()
            .Where(n => n.Status == StatusNotificacao.Enviando && n.LeaseAte < agora && n.EnvioIniciadoEm == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, StatusNotificacao.Pendente)
                .SetProperty(n => n.LeaseAte, (DateTimeOffset?)null)
                .SetProperty(n => n.ReivindicacaoId, (Guid?)null)
                .SetProperty(n => n.ProximaTentativaEm, agora)
                .SetProperty(n => n.AtualizadoEm, (DateTimeOffset?)agora), cancellationToken);

        // Queda depois de iniciar: WhatsApp pode ter sido aceito — Desconhecida, nunca reenviada sozinha.
        var ambiguos = await _dbContext.NotificacoesPendentes.IgnoreQueryFilters()
            .Where(n => n.Status == StatusNotificacao.Enviando && n.LeaseAte < agora && n.EnvioIniciadoEm != null && n.Canal == CanalNotificacao.WhatsApp)
            .Select(n => new { n.Id, n.NegocioId, n.Tipo, n.ReivindicacaoId })
            .ToListAsync(cancellationToken);

        foreach (var ambiguo in ambiguos)
        {
            var linhas = await _dbContext.NotificacoesPendentes.IgnoreQueryFilters()
                .Where(n => n.Id == ambiguo.Id && n.ReivindicacaoId == ambiguo.ReivindicacaoId && n.Status == StatusNotificacao.Enviando)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Status, StatusNotificacao.Desconhecida)
                    .SetProperty(n => n.LeaseAte, (DateTimeOffset?)null)
                    .SetProperty(n => n.ReivindicacaoId, (Guid?)null)
                    .SetProperty(n => n.UltimoErroCodigo, CodigosOutbox.LeaseExpiradoAposInicio)
                    .SetProperty(n => n.AtualizadoEm, (DateTimeOffset?)agora), cancellationToken);

            if (linhas == 1)
            {
                _logger.LogWarning(
                    "Notificação {NotificacaoId} do negócio {NegocioId} ({Tipo}/WhatsApp) perdeu a reivindicação depois de iniciar o envio: resultado desconhecido, não será reenviada.",
                    ambiguo.Id, ambiguo.NegocioId, ambiguo.Tipo);
                await AtualizarRetornoAsync(ambiguo.Id, agora, cancellationToken);
            }
        }

        // E-mail depois de iniciar: pela política (nova tentativa com a espera da família, ou encerra).
        var emails = await _dbContext.NotificacoesPendentes.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.Status == StatusNotificacao.Enviando && n.LeaseAte < agora && n.EnvioIniciadoEm != null && n.Canal == CanalNotificacao.Email)
            .ToListAsync(cancellationToken);

        foreach (var item in emails)
        {
            var transicao = _politica.AposFalhaRetentavel(item, item.Tentativas, agora, CodigosOutbox.LeaseExpiradoAposInicio);
            await AplicarFencedAsync(item, transicao, cancellationToken);
        }

        if (devolvidos + ambiguos.Count + emails.Count > 0)
            _logger.LogInformation(
                "Outbox: {Devolvidos} item(ns) devolvido(s) à fila, {Ambiguos} desconhecido(s), {Emails} e-mail(s) reagendado(s) por lease vencido.",
                devolvidos, ambiguos.Count, emails.Count);
    }

    /// <summary>
    /// Registro de retorno <c>Enviando</c> há muito tempo e sem mensagem viva na outbox (o caminho direto antigo que caiu no meio do envio):
    /// o resultado é ambíguo, então vira <c>Desconhecido</c> — nunca reenviado, fora da contagem de enviados.
    /// </summary>
    public async Task<int> RecuperarRetornosPresosAsync(CancellationToken cancellationToken)
    {
        var agora = _relogio.GetUtcNow().ToUniversalTime();
        var limite = agora.AddMinutes(-_opcoes.RetornoEnviandoPresoMinutos);
        var erro = CodigosOutbox.LeaseExpiradoAposInicio;

        var presos = await _dbContext.LembretesRetornoEnvios.IgnoreQueryFilters()
            .Where(e => e.Status == StatusLembreteRetorno.Enviando && e.EnviadoEm < limite
                && !_dbContext.NotificacoesPendentes.IgnoreQueryFilters().Any(n =>
                    n.Tipo == TipoNotificacao.LembreteRetorno && n.ClienteId == e.ClienteId && n.AgendamentoId == e.AgendamentoId
                    && (n.Status == StatusNotificacao.Pendente || n.Status == StatusNotificacao.Enviando)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, StatusLembreteRetorno.Desconhecido)
                .SetProperty(e => e.Erro, erro), cancellationToken);

        if (presos > 0)
            _logger.LogWarning("Outbox: {Quantidade} lembrete(s) de retorno preso(s) em Enviando marcado(s) como desconhecido(s); não serão reenviados.", presos);

        return presos;
    }

    public async Task VencerPrazosAsync(CancellationToken cancellationToken)
    {
        var agora = _relogio.GetUtcNow().ToUniversalTime();

        var vencidos = await _dbContext.NotificacoesPendentes.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.Status == StatusNotificacao.Pendente && n.ExpiraEm <= agora)
            .OrderBy(n => n.ExpiraEm)
            .Take(500)
            .ToListAsync(cancellationToken);

        foreach (var item in vencidos)
        {
            var transicao = PoliticaDeTentativas.Vencida(item, item.Tentativas, agora);
            var status = transicao.Status;
            var codigo = transicao.Codigo;
            DateTimeOffset? canceladaEm = status == StatusNotificacao.Cancelada ? agora : null;
            var linhas = await _dbContext.NotificacoesPendentes.IgnoreQueryFilters()
                .Where(n => n.Id == item.Id && n.Status == StatusNotificacao.Pendente && n.ExpiraEm <= agora)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Status, status)
                    .SetProperty(n => n.CanceladaEm, n => canceladaEm ?? n.CanceladaEm)
                    .SetProperty(n => n.UltimoErroCodigo, codigo)
                    .SetProperty(n => n.AtualizadoEm, (DateTimeOffset?)agora), cancellationToken);

            if (linhas == 1)
            {
                if (transicao.Status == StatusNotificacao.Falhou)
                    _logger.LogWarning(
                        "Notificação {NotificacaoId} do negócio {NegocioId} falhou definitivamente ({Tipo}/{Canal}): {Codigo} após {Tentativas} tentativa(s).",
                        item.Id, item.NegocioId, item.Tipo, item.Canal, transicao.Codigo, item.Tentativas);

                await AtualizarRetornoAsync(item.Id, agora, cancellationToken);
            }
        }
    }

    /// <summary>Grava a transição de um item recuperado, fenceada pela reivindicação que venceu.</summary>
    private async Task AplicarFencedAsync(NotificacaoPendente item, Transicao t, CancellationToken cancellationToken)
    {
        var instante = t.Agora.ToUniversalTime();
        DateTimeOffset? proxima = t.ProximaTentativaEm?.ToUniversalTime();
        DateTimeOffset? canceladaEm = t.Status == StatusNotificacao.Cancelada ? instante : null;
        var status = t.Status;
        var codigo = t.Codigo;

        var linhas = await _dbContext.NotificacoesPendentes.IgnoreQueryFilters()
            .Where(n => n.Id == item.Id && n.ReivindicacaoId == item.ReivindicacaoId && n.Status == StatusNotificacao.Enviando)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, status)
                .SetProperty(n => n.ProximaTentativaEm, n => proxima ?? n.ProximaTentativaEm)
                .SetProperty(n => n.LeaseAte, (DateTimeOffset?)null)
                .SetProperty(n => n.ReivindicacaoId, (Guid?)null)
                .SetProperty(n => n.CanceladaEm, n => canceladaEm ?? n.CanceladaEm)
                .SetProperty(n => n.UltimoErroCodigo, codigo)
                .SetProperty(n => n.AtualizadoEm, (DateTimeOffset?)instante), cancellationToken);

        if (linhas == 1)
        {
            if (status == StatusNotificacao.Falhou)
                _logger.LogWarning(
                    "Notificação {NotificacaoId} do negócio {NegocioId} falhou definitivamente ({Tipo}/{Canal}): {Codigo} após {Tentativas} tentativa(s).",
                    item.Id, item.NegocioId, item.Tipo, item.Canal, codigo, item.Tentativas);

            await AtualizarRetornoAsync(item.Id, instante, cancellationToken);
        }
    }

    /// <summary>
    /// Itens de retorno encerrados fora do despacho (prazo vencido, lease vencido) também fecham o registro de relatório e de
    /// idempotência do lembrete — o que nunca "sai do ar" como <c>Enviando</c>.
    /// </summary>
    private async Task AtualizarRetornoAsync(Guid notificacaoId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var item = await _dbContext.NotificacoesPendentes.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == notificacaoId, cancellationToken);
        if (item is not { Tipo: TipoNotificacao.LembreteRetorno } || item.Status == StatusNotificacao.Pendente)
            return;

        _dbContext.ChangeTracker.Clear();
        var envio = await _dbContext.LembretesRetornoEnvios.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.ClienteId == item.ClienteId && e.AgendamentoId == item.AgendamentoId, cancellationToken);
        if (envio is null)
            return;

        var codigo = item.UltimoErroCodigo ?? CodigosOutbox.ErroInterno;
        if (item.Status == StatusNotificacao.Desconhecida)
            envio.MarcarDesconhecido(codigo, agora);
        else if (item.Status == StatusNotificacao.Enviada)
            envio.MarcarEnviado(item.IdMensagemProvedor, agora);
        else
            envio.MarcarFalha(codigo, agora);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
    }
}
