using System.Reflection;
using FluentAssertions;
using Hangfire;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Clientes;
using Plataforma.Infraestrutura.Notificacoes.Outbox;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>
/// B7.4-A: os jobs de lembrete são recorrentes e enviam mensagens. Duas execuções juntas enviariam a mesma mensagem duas vezes e um
/// retry imediato reenviaria o que já saiu; o próximo ciclo do cron já é a nova tentativa. A política fica explícita no método que o
/// Hangfire invoca (<c>RegistroJobsRecorrentes</c> chama <c>ExecutarAsync(CancellationToken)</c>).
/// </summary>
public class PoliticaHangfireDosJobsTestes
{
    public static IEnumerable<object[]> Jobs() => [[typeof(JobEnviarLembretes)], [typeof(JobLembreteRetorno)], [typeof(JobDespacharNotificacoes)]];

    private static MethodInfo MetodoDoHangfire(Type job) =>
        job.GetMethod("ExecutarAsync", [typeof(CancellationToken)]) ?? throw new InvalidOperationException("ExecutarAsync(CancellationToken) não encontrado.");

    [Theory, MemberData(nameof(Jobs))]
    public void Execucao_concorrente_e_desligada_com_espera_curta(Type job)
    {
        var atributo = MetodoDoHangfire(job).GetCustomAttribute<DisableConcurrentExecutionAttribute>();

        atributo.Should().NotBeNull();
        atributo!.TimeoutSec.Should().BeInRange(1, 30, "a segunda execução desiste logo; o próximo ciclo já tenta de novo");
    }

    [Theory, MemberData(nameof(Jobs))]
    public void Retry_automatico_do_hangfire_e_desligado(Type job)
    {
        var atributo = MetodoDoHangfire(job).GetCustomAttribute<AutomaticRetryAttribute>();

        atributo.Should().NotBeNull();
        atributo!.Attempts.Should().Be(0);
    }
}
