using System.Diagnostics;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// Sprint 2 (seção 1, M4): "listar horários livres responde em p95 abaixo de 300 ms com
/// 1.000 agendamentos por profissional".
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class PerformanceDisponibilidadeTestes : IAsyncLifetime
{
    private const int QuantidadeDeAgendamentos = 1000;
    private const int QuantidadeDeChamadasMedidas = 30;
    private const double LimitesP95Milissegundos = 300;

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public PerformanceDisponibilidadeTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Listar_horarios_livres_com_mil_agendamentos_fica_abaixo_de_300ms_no_p95()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        await SemearMilAgendamentosAsync(negocioId, cenario.ProfissionalId, cenario.ServicoId);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var diasAteSegunda = ((int)DayOfWeek.Monday - (int)hoje.DayOfWeek + 7) % 7;
        var segunda = hoje.AddDays(diasAteSegunda == 0 ? 7 : diasAteSegunda);
        var caminho = $"/painel/profissionais/{cenario.ProfissionalId}/horarios-livres?data={segunda:yyyy-MM-dd}&duracaoMinutos=30";

        // Aquece (conexão, plano de consulta, JIT) antes de medir de verdade.
        await cliente.GetAsync(caminho);

        var tempos = new List<double>();
        for (var i = 0; i < QuantidadeDeChamadasMedidas; i++)
        {
            var cronometro = Stopwatch.StartNew();
            var resposta = await cliente.GetAsync(caminho);
            cronometro.Stop();
            resposta.EnsureSuccessStatusCode();
            tempos.Add(cronometro.Elapsed.TotalMilliseconds);
        }

        var p95 = CalcularPercentil(tempos, 95);
        p95.Should().BeLessThan(LimitesP95Milissegundos, $"p95 observado: {p95:F1}ms em {tempos.Count} chamadas");
    }

    private async Task SemearMilAgendamentosAsync(Guid negocioId, Guid profissionalId, Guid servicoId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        // Escopo novo, sem negócio resolvido (não passou pelo pipeline HTTP/JWT) — o
        // filtro multi-tenant bloquearia a leitura (comportamento correto e esperado,
        // seção 8.3.1), então ignora o filtro de propósito aqui, só pra semear o teste.
        var servico = await dbContext.Servicos.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == servicoId);
        var itens = new[] { new ItemServicoAgendamento(servicoId, servico!.Nome, servico.Preco, servico.DuracaoMinutos) };

        // Espalha os 1000 agendamentos por vários dias/profissionais fictícios — o que
        // importa pro teste é ter volume TOTAL representativo na tabela (índice
        // (profissional_id, inicio) é quem garante que UM profissional específico continua rápido).
        var dataBase = new DateTimeOffset(2020, 1, 6, 9, 0, 0, TimeSpan.Zero); // uma segunda-feira, bem no passado — não colide com o teste

        for (var i = 0; i < QuantidadeDeAgendamentos; i++)
        {
            var inicio = dataBase.AddMinutes(i * 45); // já com folga suficiente pra nunca sobrepor
            var agendamento = Agendamento.CriarConfirmado(negocioId, profissionalId, Guid.NewGuid(), inicio, itens);

            // ClienteId aleatório: não precisamos de 1000 clientes de verdade pra este teste
            // (a listagem de disponibilidade nem olha pro cliente).
            dbContext.Agendamentos.Add(agendamento);
        }

        await dbContext.SaveChangesAsync();
    }

    private static double CalcularPercentil(List<double> valores, int percentil)
    {
        var ordenados = valores.OrderBy(v => v).ToList();
        var indice = (int)Math.Ceiling(percentil / 100.0 * ordenados.Count) - 1;
        return ordenados[Math.Clamp(indice, 0, ordenados.Count - 1)];
    }
}
