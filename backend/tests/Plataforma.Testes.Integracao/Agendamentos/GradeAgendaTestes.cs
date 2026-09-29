using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>Visão "Dia" da agenda em grade — profissionais em coluna, horários de 15 min em linha (seção 7, item 11).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class GradeAgendaTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public GradeAgendaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Grade_mostra_livre_ocupado_almoco_folga_bloqueio_e_fora_do_expediente_de_cada_profissional()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        // "Profissional de Teste": 09–12 e 13–18, de segunda a sexta.
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegunda();

        var (meioPeriodo, semExpediente, inativo) = await _fabrica.NoBancoAsync(async db =>
        {
            var meio = Profissional.Criar(negocioId, "Meio Período");
            var semHorario = Profissional.Criar(negocioId, "Sem Horário Hoje");
            var desligado = Profissional.Criar(negocioId, "Inativo");
            desligado.Desativar();
            db.Profissionais.AddRange(meio, semHorario, desligado);
            db.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocioId, meio.Id, DiaSemana.Segunda, new TimeOnly(10, 0), new TimeOnly(14, 0)));
            db.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocioId, desligado.Id, DiaSemana.Segunda, new TimeOnly(9, 0), new TimeOnly(18, 0)));
            db.BloqueiosAgenda.Add(BloqueioAgenda.Criar(
                negocioId, cenario.ProfissionalId, Local(segunda, 16, 0), Local(segunda, 17, 0), "Médico"));
            await db.SaveChangesAsync();
            return (meio.Id, semHorario.Id, desligado.Id);
        });

        var agendamentoId = await (await admin.PostAsJsonAsync("/painel/agendamentos",
            new CriarAgendamento(cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], Local(segunda, 10, 0))))
            .Content.ReadFromJsonAsync<Guid>();

        var resposta = await admin.GetAsync($"/painel/agenda/grade?data={segunda:yyyy-MM-dd}");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var grade = (await resposta.Content.ReadFromJsonAsync<GradeAgenda>())!;

        // Do primeiro turno do dia (09:00) ao último (18:00), de 15 em 15 minutos.
        grade.Horarios.First().Should().Be("09:00");
        grade.Horarios.Last().Should().Be("17:45");
        grade.Horarios.Should().HaveCount(36);

        // Só profissionais ativos, um por coluna — inclusive quem não tem usuário (nenhum aqui tem).
        grade.Profissionais.Select(p => p.ProfissionalId).Should().BeEquivalentTo([cenario.ProfissionalId, meioPeriodo, semExpediente]);
        grade.Profissionais.Should().NotContain(p => p.ProfissionalId == inativo);

        var principal = grade.Profissionais.Single(p => p.ProfissionalId == cenario.ProfissionalId);
        Estado(principal, "09:00").Should().Be(EstadoCelulaGrade.Livre);
        Estado(principal, "10:00").Should().Be(EstadoCelulaGrade.Ocupado);
        Estado(principal, "10:15").Should().Be(EstadoCelulaGrade.Ocupado);
        Estado(principal, "10:30").Should().Be(EstadoCelulaGrade.Livre);
        Estado(principal, "12:00").Should().Be(EstadoCelulaGrade.Almoco);
        Estado(principal, "12:45").Should().Be(EstadoCelulaGrade.Almoco);
        Estado(principal, "13:00").Should().Be(EstadoCelulaGrade.Livre);
        Estado(principal, "16:00").Should().Be(EstadoCelulaGrade.Bloqueio);
        principal.Celulas.Single(c => c.Hora == "16:45").Descricao.Should().Be("Médico");
        Estado(principal, "17:00").Should().Be(EstadoCelulaGrade.Livre);
        principal.DeFolga.Should().BeFalse();
        principal.Celulas.Single(c => c.Hora == "10:00").AgendamentoId.Should().Be(agendamentoId);
        principal.Celulas.Single(c => c.Hora == "10:00").Inicio.Should().Be(Local(segunda, 10, 0));
        principal.Agendamentos.Should().ContainSingle(a => a.Id == agendamentoId && a.ClienteNome == "Cliente de Teste" && a.Status == "Agendado");

        var meio = grade.Profissionais.Single(p => p.ProfissionalId == meioPeriodo);
        Estado(meio, "09:45").Should().Be(EstadoCelulaGrade.ForaDoExpediente);
        Estado(meio, "10:00").Should().Be(EstadoCelulaGrade.Livre);
        Estado(meio, "14:00").Should().Be(EstadoCelulaGrade.ForaDoExpediente);

        var folga = grade.Profissionais.Single(p => p.ProfissionalId == semExpediente);
        folga.DeFolga.Should().BeTrue();
        folga.Celulas.Should().OnlyContain(c => c.Estado == EstadoCelulaGrade.Folga);

        // Cancelado libera a célula.
        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/cancelar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var depois = (await admin.GetFromJsonAsync<GradeAgenda>($"/painel/agenda/grade?data={segunda:yyyy-MM-dd}"))!;
        Estado(depois.Profissionais.Single(p => p.ProfissionalId == cenario.ProfissionalId), "10:00").Should().Be(EstadoCelulaGrade.Livre);
    }

    [Fact]
    public async Task Grade_exige_gerenciar_agenda()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        using var profissional = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional);

        (await profissional.GetAsync($"/painel/agenda/grade?data={ProximaSegunda():yyyy-MM-dd}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static string Estado(ColunaGrade coluna, string hora) => coluna.Celulas.Single(c => c.Hora == hora).Estado;

    private static DateOnly ProximaSegunda()
    {
        var dia = SemeadorDeComissoes.Dia(3);
        while (dia.DayOfWeek != DayOfWeek.Monday)
            dia = dia.AddDays(1);
        return dia;
    }

    private static DateTimeOffset Local(DateOnly dia, int hora, int minuto) => SemeadorDeComissoes.Local(dia, hora, minuto);
}
