using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.Infraestrutura.SemeadorDeComissoes;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>Visão "Semana" e a agenda do Profissional logado (seção 7: "a própria agenda, dia e semana").</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AgendaSemanaTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public AgendaSemanaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    /// <summary>A próxima segunda-feira no fuso do negócio (a semana de teste começa nela).</summary>
    private static DateOnly ProximaSegunda()
    {
        var dia = Dia(7);
        while (dia.DayOfWeek != DayOfWeek.Monday)
            dia = dia.AddDays(1);
        return dia;
    }

    [Fact]
    public async Task Semana_mostra_expediente_folga_bloqueio_e_agendamentos_de_cada_dia()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegunda();

        await _fabrica.SemearAgendamentoAsync(negocioId, cenario.ProfissionalId, Local(segunda.AddDays(1), 10, 0));
        await _fabrica.NoBancoAsync(async db =>
        {
            db.BloqueiosAgenda.Add(BloqueioAgenda.Criar(negocioId, cenario.ProfissionalId, Local(segunda.AddDays(2), 14, 0), Local(segunda.AddDays(2), 16, 0), "Médico"));
            await db.SaveChangesAsync();
            return 0;
        });

        var semana = await admin.GetFromJsonAsync<AgendaSemana>(
            $"/painel/agenda/semana?profissionalId={cenario.ProfissionalId}&inicio={segunda:yyyy-MM-dd}");

        semana!.Dias.Should().HaveCount(7);
        semana.Dias[0].Turnos.Should().Equal("09:00–12:00", "13:00–18:00");
        semana.Dias[0].Folga.Should().BeFalse();
        semana.Dias[5].Folga.Should().BeTrue(); // sábado
        semana.Dias[1].Agendamentos.Should().ContainSingle(a => a.Status == nameof(StatusAgendamento.Agendado));
        semana.Dias.Where((_, i) => i != 1).Should().OnlyContain(d => d.Agendamentos.Count == 0);
        semana.Dias[2].Bloqueios.Should().Equal("Bloqueado 14:00–16:00 (Médico)");

        (await admin.GetAsync($"/painel/agenda/semana?profissionalId={Guid.NewGuid()}&inicio={segunda:yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Bug real: o início de um turno saía com o offset do fuso (-03:00) e o horário depois de um agendamento em UTC. A tela
    /// compara os textos (grade × horários livres) e mostrava o mesmo horário duas vezes.
    /// </summary>
    [Fact]
    public async Task Horarios_livres_e_linhas_da_grade_saem_sempre_em_utc()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegunda();
        await _fabrica.SemearAgendamentoAsync(negocioId, cenario.ProfissionalId, Local(segunda, 10, 0));

        var livres = await admin.GetStringAsync(
            $"/painel/profissionais/{cenario.ProfissionalId}/horarios-livres?data={segunda:yyyy-MM-dd}&duracaoMinutos=30");
        var grade = await admin.GetStringAsync($"/painel/agenda/grade?data={segunda:yyyy-MM-dd}");

        livres.Should().Contain("T12:00:00+00:00").And.NotContain("-03:00");
        grade.Should().Contain("T12:00:00+00:00").And.NotContain("-03:00");
    }

    [Fact]
    public async Task Profissional_logado_ve_a_propria_agenda_e_toca_so_os_proprios_atendimentos()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var outroProfissional = await _fabrica.CriarProfissionalAsync(negocioId, "Outro");
        var hoje = Dia(0);

        var meu = await _fabrica.SemearAgendamentoAsync(negocioId, cenario.ProfissionalId, DateTimeOffset.UtcNow.AddMinutes(-30));
        var dele = await _fabrica.SemearAgendamentoAsync(negocioId, outroProfissional, DateTimeOffset.UtcNow.AddMinutes(-30));

        using var profissional = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, cenario.ProfissionalId);

        // Sem "gerenciar agenda": a agenda geral é 403, a própria responde.
        (await profissional.GetAsync($"/painel/agenda?profissionalId={cenario.ProfissionalId}&data={hoje:yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var dia = await profissional.GetFromJsonAsync<List<AgendamentoResumo>>($"/painel/minha-agenda?data={Dia(-1):yyyy-MM-dd}")
            ?? [];
        dia.AddRange((await profissional.GetFromJsonAsync<List<AgendamentoResumo>>($"/painel/minha-agenda?data={hoje:yyyy-MM-dd}"))!);
        dia.Should().ContainSingle(a => a.Id == meu).And.NotContain(a => a.Id == dele);

        var semana = await profissional.GetFromJsonAsync<AgendaSemana>($"/painel/minha-agenda/semana?inicio={Dia(-1):yyyy-MM-dd}");
        semana!.ProfissionalId.Should().Be(cenario.ProfissionalId);

        // Iniciar e concluir o próprio; o do outro não existe para ele.
        (await profissional.PostAsync($"/painel/minha-agenda/{meu}/iniciar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await profissional.PostAsync($"/painel/minha-agenda/{meu}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await profissional.PostAsync($"/painel/minha-agenda/{meu}/faltou", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await profissional.PostAsync($"/painel/minha-agenda/{dele}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Remarcar e cancelar continuam com quem gerencia a agenda.
        (await profissional.PostAsync($"/painel/agendamentos/{meu}/cancelar", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var status = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters()
            .Where(a => a.Id == meu || a.Id == dele).ToDictionaryAsync(a => a.Id, a => a.Status));
        status[meu].Should().Be(StatusAgendamento.Concluido);
        status[dele].Should().Be(StatusAgendamento.Agendado);

        // Sem vínculo com profissional, não há "minha agenda".
        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista);
        (await recepcao.GetAsync($"/painel/minha-agenda?data={hoje:yyyy-MM-dd}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await recepcao.PostAsync($"/painel/minha-agenda/{dele}/iniciar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
