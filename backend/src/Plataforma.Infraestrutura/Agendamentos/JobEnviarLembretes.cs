using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Lembrete de 60 minutos antes do agendamento (seção 9), rodando a cada poucos minutos em vez
/// de um job agendado por agendamento — sobrevive à hibernação da API dos planos gratuitos
/// (seção 8.5.6: ao voltar, envia os pendentes ainda válidos e descarta os vencidos) sem
/// depender do agendador de delay do Hangfire ter disparado no momento exato.
/// <para>
/// Este job só orquestra: descobre quais negócios têm lembrete a enviar e entrega cada um a um
/// <see cref="ProcessadorLembretesDoNegocio"/> num escopo de DI NOVO (o contexto de negócio é scoped e não troca de tenant). Falha
/// em um negócio não interrompe os outros.
/// </para>
/// <para>
/// Política do Hangfire: <c>DisableConcurrentExecution</c> (duas execuções juntas enviariam o mesmo lembrete duas vezes; a espera é
/// de 10 s e a segunda desiste — medido: uma trava órfã de um processo que caiu só é liberada pelo Hangfire.PostgreSql depois de ~10 min,
/// ou seja, no máximo dois ciclos sem lembrete, sempre dentro da janela de 60 min) e nenhum
/// retry automático — o job é recorrente, o próximo ciclo (5 min) já é a nova tentativa, e um retry imediato reenviaria o que o
/// negócio anterior já mandou.
/// </para>
/// </summary>
public sealed class JobEnviarLembretes
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IServiceScopeFactory _escopos;
    private readonly OpcoesLembretes _opcoesLembretes;
    private readonly ILogger<JobEnviarLembretes> _logger;

    public JobEnviarLembretes(
        PlataformaDbContext dbContext, IServiceScopeFactory escopos, IOptions<OpcoesLembretes> opcoesLembretes, ILogger<JobEnviarLembretes> logger)
    {
        _dbContext = dbContext;
        _escopos = escopos;
        _opcoesLembretes = opcoesLembretes.Value;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 10)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecutarAsync(CancellationToken cancellationToken = default)
    {
        var agora = DateTimeOffset.UtcNow;
        var antecedencia = TimeSpan.FromMinutes(_opcoesLembretes.AntecedenciaMinutos);
        var limite = agora + antecedencia;

        // Descarta os vencidos (seção 8.5.6): agendamento cujo horário já passou nunca mais
        // recebe lembrete, mesmo que o job tenha ficado muito tempo sem rodar.
        await _dbContext.Agendamentos.IgnoreQueryFilters()
            .Where(a => a.Status == StatusAgendamento.Agendado && a.Inicio <= agora && !a.LembreteEnviado)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LembreteEnviado, true), cancellationToken);

        // O job varre todos os negócios (varredura administrativa, só ids): sem negócio resolvido, o filtro global não se aplica.
        var negocios = await _dbContext.Agendamentos.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Status == StatusAgendamento.Agendado && !a.LembreteEnviado && a.Inicio > agora && a.Inicio <= limite)
            .Select(a => a.NegocioId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

        foreach (var negocioId in negocios)
        {
            try
            {
                using var escopo = _escopos.CreateScope();
                await escopo.ServiceProvider.GetRequiredService<ProcessadorLembretesDoNegocio>()
                    .ProcessarAsync(negocioId, agora, cancellationToken);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                // Só o id técnico e o tipo do erro: a mensagem de uma exceção do banco pode carregar valores de colunas.
                _logger.LogError("Falha ao processar os lembretes do negócio {NegocioId} ({TipoErro}).", negocioId, excecao.GetType().Name);
            }
        }
    }
}
