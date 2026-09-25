using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Infraestrutura.Jobs;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>
/// Lock do Hangfire preso por um container anterior não pode derrubar a subida da API:
/// o registro dos jobs recorrentes tenta de novo em segundo plano até conseguir.
/// </summary>
public class RegistroJobsRecorrentesTestes
{
    private sealed class GerenciadorQueFalha(int falhasAntesDeFuncionar) : IRecurringJobManager
    {
        public int Tentativas;
        public List<string> Registrados { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        {
            if (recurringJobId == "expirar-reservas" && ++Tentativas <= falhasAntesDeFuncionar)
                throw new TimeoutException("Timeout expired obtaining a distributed lock.");
            Registrados.Add(recurringJobId);
        }

        public void Trigger(string recurringJobId) { }
        public void RemoveIfExists(string recurringJobId) { }
    }

    private static RegistroJobsRecorrentes CriarServico(IRecurringJobManager gerenciador)
    {
        var servicos = new ServiceCollection().AddSingleton(gerenciador).BuildServiceProvider();
        return new RegistroJobsRecorrentes(servicos, NullLogger<RegistroJobsRecorrentes>.Instance, TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task Registra_os_tres_jobs_de_primeira_quando_nada_falha()
    {
        var gerenciador = new GerenciadorQueFalha(falhasAntesDeFuncionar: 0);

        var servico = CriarServico(gerenciador);
        await servico.StartAsync(CancellationToken.None);
        await servico.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        gerenciador.Registrados.Should().BeEquivalentTo("expirar-reservas", "enviar-lembretes", "atualizar-assinaturas");
    }

    [Fact]
    public async Task Falha_ao_registrar_nao_propaga_e_tenta_de_novo_ate_conseguir()
    {
        var gerenciador = new GerenciadorQueFalha(falhasAntesDeFuncionar: 2);
        var servico = CriarServico(gerenciador);

        // StartAsync não pode lançar — é o que antes derrubava a API na subida.
        var subir = () => servico.StartAsync(CancellationToken.None);
        await subir.Should().NotThrowAsync();

        await servico.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        gerenciador.Tentativas.Should().Be(3);
        gerenciador.Registrados.Should().Contain(["expirar-reservas", "enviar-lembretes", "atualizar-assinaturas"]);
    }

    [Fact]
    public async Task Para_de_tentar_quando_a_aplicacao_esta_encerrando()
    {
        var gerenciador = new GerenciadorQueFalha(falhasAntesDeFuncionar: int.MaxValue);
        var servico = CriarServico(gerenciador);

        await servico.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await servico.StopAsync(CancellationToken.None);

        servico.ExecuteTask!.IsCompleted.Should().BeTrue();
        gerenciador.Registrados.Should().BeEmpty();
    }
}
