using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// Sprint 2 (seção 1, M2 / seção 8.2, "Testes obrigatórios"): "100 requisições
/// simultâneas para o mesmo horário resultam em exatamente 1 agendamento". A garantia
/// real é a exclusion constraint do Postgres (seção 8.2.1) — este teste prova que ela
/// segura a barra mesmo com toda a aplicação martelando ao mesmo tempo.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ConcorrenciaTestes : IAsyncLifetime
{
    private const int QuantidadeDeRequisicoesSimultaneas = 100;

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public ConcorrenciaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Cem_requisicoes_simultaneas_para_o_mesmo_horario_resultam_em_exatamente_um_agendamento()
    {
        var (clientePrincipal, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var diasAteSegunda = ((int)DayOfWeek.Monday - (int)hoje.DayOfWeek + 7) % 7;
        var segunda = hoje.AddDays(diasAteSegunda == 0 ? 7 : diasAteSegunda);
        var inicio = new DateTimeOffset(segunda.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Unspecified), TimeSpan.FromHours(-3));

        var dadosDoAgendamento = new CriarAgendamento(cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio);

        // As 100 requisições precisam mirar o MESMO negócio/profissional do cenário —
        // por isso reusa o mesmo cliente autenticado (HttpClient é seguro para uso
        // concorrente) em vez de criar 100 usuários, o que criaria 100 negócios diferentes.
        var tarefas = Enumerable.Range(0, QuantidadeDeRequisicoesSimultaneas)
            .Select(_ => clientePrincipal.PostAsJsonAsync("/painel/agendamentos", dadosDoAgendamento));
        var respostas = await Task.WhenAll(tarefas);

        var sucessos = respostas.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflitos = respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        sucessos.Should().Be(1, "exatamente uma requisição deveria vencer a corrida");
        conflitos.Should().Be(QuantidadeDeRequisicoesSimultaneas - 1, "todas as outras deveriam perder por conflito de horário");

        // Confere direto no banco também — não só pela contagem de respostas HTTP.
        var agenda = await clientePrincipal.GetFromJsonAsync<List<AgendamentoResumo>>(
            $"/painel/agenda?profissionalId={cenario.ProfissionalId}&data={segunda:yyyy-MM-dd}");
        agenda.Should().ContainSingle();

        foreach (var resposta in respostas)
            resposta.Dispose();
    }
}
