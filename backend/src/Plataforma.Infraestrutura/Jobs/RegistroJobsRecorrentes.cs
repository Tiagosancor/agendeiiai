using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Assinaturas;

namespace Plataforma.Infraestrutura.Jobs;

/// <summary>
/// Registra os jobs recorrentes do Hangfire em segundo plano, tentando de novo até conseguir.
/// Antes o registro rodava direto no Program.cs, antes do servidor subir: se o container
/// anterior morresse segurando um lock distribuído do Hangfire (deploy, reinício forçado),
/// o <c>AddOrUpdate</c> estourava o tempo esperando esse lock e derrubava a API inteira, em
/// loop, até o lock vencer sozinho (~10 min). Aqui, a falha só adia o registro — a API sobe
/// e atende normalmente, e os jobs já gravados de execuções anteriores continuam rodando.
/// </summary>
public sealed class RegistroJobsRecorrentes(
    IServiceProvider servicos,
    ILogger<RegistroJobsRecorrentes> logger,
    TimeSpan? intervaloEntreTentativas = null) : BackgroundService
{
    private readonly TimeSpan _intervalo = intervaloEntreTentativas ?? TimeSpan.FromSeconds(30);

    /// <summary>Os três jobs recorrentes do sistema — idempotente (upsert por id).</summary>
    public static void Registrar(IRecurringJobManager gerenciador)
    {
        // Expira reservas vencidas em todo o sistema, a cada minuto (seção 8.2.2) — cobre o
        // caso de alguém reservar um horário e abandonar o fluxo.
        gerenciador.AddOrUpdate<JobExpirarReservas>(
            "expirar-reservas", job => job.ExecutarAsync(CancellationToken.None), "*/1 * * * *");

        // Lembretes 24h/2h antes (seção 9, Sprint 4) — varre em vez de agendar um job por
        // agendamento, justamente para sobreviver à hibernação da API (seção 8.5.6).
        gerenciador.AddOrUpdate<JobEnviarLembretes>(
            "enviar-lembretes", job => job.ExecutarAsync(CancellationToken.None), "*/5 * * * *");

        // Teste → carência → suspensa e avisos de 7/3/1 dias (seção 7). De hora em hora:
        // mesma varredura idempotente, sem um negócio passar quase um dia no estado errado
        // se a API hibernar no horário do job.
        gerenciador.AddOrUpdate<JobAtualizarAssinaturas>(
            "atualizar-assinaturas", job => job.ExecutarAsync(CancellationToken.None), "0 * * * *");
    }

    protected override async Task ExecuteAsync(CancellationToken cancelamento)
    {
        while (!cancelamento.IsCancellationRequested)
        {
            try
            {
                // API baseada em serviço (não a estática RecurringJob.*): a estática depende do
                // singleton global JobStorage.Current, que não isola entre containers de DI
                // diferentes (quebra o WebApplicationFactory dos testes de integração).
                Registrar(servicos.GetRequiredService<IRecurringJobManager>());
                logger.LogInformation("Jobs recorrentes registrados.");
                return;
            }
            catch (Exception excecao) when (!cancelamento.IsCancellationRequested)
            {
                logger.LogWarning(excecao,
                    "Não foi possível registrar os jobs recorrentes agora; nova tentativa em {Intervalo}.", _intervalo);
            }

            try
            {
                await Task.Delay(_intervalo, cancelamento);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
