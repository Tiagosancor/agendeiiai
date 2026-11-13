using System.Diagnostics;
using Serilog;

namespace Plataforma.Api;

/// <summary>
/// Diagnóstico de reinício em loop sem erro no log: nos primeiros segundos de vida do processo, registra a cada 250 ms a memória, as
/// threads e os limites/contadores do cgroup do contêiner. A última linha antes de uma morte súbita mostra se o processo chegou perto do
/// limite de memória (OOM) ou de threads; <c>oom_kill</c> acumulado em <c>memory.events</c> prova que o kernel já matou o contêiner.
/// Não loga dado pessoal nem configuração.
/// </summary>
public static class DiagnosticoProcesso
{
    private const string CgroupV2 = "/sys/fs/cgroup/";

    public static void IniciarSentinela(TimeSpan duracao, TimeSpan intervalo)
    {
        var thread = new Thread(() =>
        {
            try
            {
                Log.Information("Sentinela: cgroup memory.max={Max} pids.max={PidsMax} memory.events=[{Eventos}] processadores={Cpus}.",
                    Ler("memory.max"), Ler("pids.max"), Ler("memory.events")?.Replace('\n', ' '), Environment.ProcessorCount);

                var limite = Stopwatch.StartNew();
                while (limite.Elapsed < duracao)
                {
                    using var processo = Process.GetCurrentProcess();
                    Log.Information(
                        "Sentinela t={Ms}ms: workingSet={Ws}MB gc={Gc}MB threads={Threads} cgroupMem={Mem} pids={Pids} oom_kill={Oom}.",
                        limite.ElapsedMilliseconds, processo.WorkingSet64 / 1024 / 1024, GC.GetTotalMemory(false) / 1024 / 1024,
                        processo.Threads.Count, Ler("memory.current"), Ler("pids.current"), OomKill());
                    Thread.Sleep(intervalo);
                }
            }
            catch (Exception excecao)
            {
                Log.Warning("Sentinela interrompida: {Tipo}.", excecao.GetType().Name);
            }
        })
        {
            IsBackground = true,
            Name = "sentinela-diagnostico",
        };
        thread.Start();
    }

    private static string? Ler(string arquivo)
    {
        try
        {
            var caminho = CgroupV2 + arquivo;
            return File.Exists(caminho) ? File.ReadAllText(caminho).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? OomKill() =>
        Ler("memory.events")?.Split('\n').FirstOrDefault(l => l.StartsWith("oom_kill ", StringComparison.Ordinal))?[9..];
}
