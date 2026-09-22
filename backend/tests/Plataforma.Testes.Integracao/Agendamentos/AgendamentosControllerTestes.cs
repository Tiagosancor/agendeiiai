using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// Sprint 2 (seção 8.2 e seção 11, M): sobreposição parcial, almoço, folga, reserva
/// expirada liberando o horário e cancelamento liberando o horário. O teste de
/// concorrência (100 requisições, 1 vencedora) está em <see cref="ConcorrenciaTestes"/>;
/// o de performance (p95) em <see cref="PerformanceDisponibilidadeTestes"/>.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AgendamentosControllerTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public AgendamentosControllerTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    /// <summary>Próxima segunda-feira, sempre no futuro — o cenário padrão tem expediente de segunda a sexta.</summary>
    private static DateOnly ProximaSegundaFeira()
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)); // folga de 2 dias evita qualquer problema de "horário já passou"
        var diasAteSegunda = ((int)DayOfWeek.Monday - (int)hoje.DayOfWeek + 7) % 7;
        return hoje.AddDays(diasAteSegunda == 0 ? 7 : diasAteSegunda);
    }

    private static DateTimeOffset AsDataHora(DateOnly dia, TimeOnly hora) =>
        new(dia.ToDateTime(hora, DateTimeKind.Unspecified), TimeSpan.FromHours(-3)); // America/Sao_Paulo, sem horário de verão

    [Fact]
    public async Task Criar_agendamento_no_expediente_funciona_e_aparece_na_agenda_do_dia()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegundaFeira();
        var inicio = AsDataHora(segunda, new TimeOnly(10, 0));

        var resposta = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        var agenda = await cliente.GetFromJsonAsync<List<AgendamentoResumo>>(
            $"/painel/agenda?profissionalId={cenario.ProfissionalId}&data={segunda:yyyy-MM-dd}");

        agenda.Should().ContainSingle(a => a.Status == "Agendado");
    }

    [Fact]
    public async Task Criar_agendamento_no_horario_de_almoco_da_400()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegundaFeira();
        var inicioNoAlmoco = AsDataHora(segunda, new TimeOnly(12, 15));

        var resposta = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicioNoAlmoco));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Criar_agendamento_fora_do_expediente_da_400()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegundaFeira();
        var inicioForaDoExpediente = AsDataHora(segunda, new TimeOnly(20, 0));

        var resposta = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicioForaDoExpediente));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Criar_agendamento_sobrepondo_outro_da_409_com_sugestoes()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId, duracaoMinutos: 60);
        var segunda = ProximaSegundaFeira();
        var inicio = AsDataHora(segunda, new TimeOnly(10, 0));

        (await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Sobreposição parcial: começa 30 min depois do primeiro (que dura 60 min).
        var inicioSobreposto = inicio.AddMinutes(30);
        var respostaConflito = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicioSobreposto));

        respostaConflito.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Criar_agendamento_em_horario_bloqueado_da_400()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegundaFeira();
        var inicioBloqueio = AsDataHora(segunda, new TimeOnly(9, 0));
        var fimBloqueio = AsDataHora(segunda, new TimeOnly(18, 0));

        (await cliente.PostAsJsonAsync($"/painel/profissionais/{cenario.ProfissionalId}/bloqueios",
            new { InicioUtc = inicioBloqueio, FimUtc = fimBloqueio, Motivo = "Folga" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var resposta = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(10, 0))));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancelar_agendamento_libera_o_horario_imediatamente()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegundaFeira();
        var inicio = AsDataHora(segunda, new TimeOnly(10, 0));

        var respostaCriar = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio));
        var id = await respostaCriar.Content.ReadFromJsonAsync<Guid>();

        (await cliente.PostAsync($"/painel/agendamentos/{id}/cancelar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // O mesmo horário, pro mesmo serviço, agora tem que estar livre de novo.
        var respostaNovoAgendamento = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio));

        respostaNovoAgendamento.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Reserva_expirada_libera_o_horario()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegundaFeira();
        var inicio = AsDataHora(segunda, new TimeOnly(10, 0));

        var respostaReserva = await cliente.PostAsJsonAsync("/painel/agendamentos/reserva", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio));
        respostaReserva.StatusCode.Should().Be(HttpStatusCode.Created);
        var idReserva = await respostaReserva.Content.ReadFromJsonAsync<Guid>();

        // Expira a reserva manualmente no banco (simula os 10 minutos passando) — testar
        // isso de verdade esperando 10 min de relógio deixaria a suíte lentíssima.
        await ExpirarReservaManualmenteAsync(idReserva);

        // Tentar CRIAR OUTRO agendamento no mesmo horário precisa funcionar: a criação
        // expira reservas vencidas do profissional antes de checar disponibilidade
        // (seção 8.2.2) — não precisa esperar o job rodar.
        var respostaNovoAgendamento = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio));

        respostaNovoAgendamento.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Job_de_expiracao_expira_reservas_vencidas_de_qualquer_profissional()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegundaFeira();
        var inicio = AsDataHora(segunda, new TimeOnly(10, 0));

        var respostaReserva = await cliente.PostAsJsonAsync("/painel/agendamentos/reserva", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio));
        var idReserva = await respostaReserva.Content.ReadFromJsonAsync<Guid>();
        await ExpirarReservaManualmenteAsync(idReserva);

        using var escopo = _fabrica.Services.CreateScope();
        var job = escopo.ServiceProvider.GetRequiredService<Plataforma.Infraestrutura.Agendamentos.JobExpirarReservas>();
        await job.ExecutarAsync();

        var agenda = await cliente.GetFromJsonAsync<List<AgendamentoResumo>>(
            $"/painel/agenda?profissionalId={cenario.ProfissionalId}&data={segunda:yyyy-MM-dd}");

        agenda.Should().BeEmpty(); // Expirado não aparece na agenda do dia.
    }

    private async Task ExpirarReservaManualmenteAsync(Guid agendamentoId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<Plataforma.Infraestrutura.Persistencia.PlataformaDbContext>();
        var agendamento = await dbContext.Agendamentos.IgnoreQueryFilters()
            .FirstAsync(a => a.Id == agendamentoId);

        var campo = typeof(Plataforma.Dominio.Agendamentos.Agendamento)
            .GetProperty(nameof(Plataforma.Dominio.Agendamentos.Agendamento.ReservadoAte))!;
        campo.SetValue(agendamento, DateTimeOffset.UtcNow.AddMinutes(-1));

        await dbContext.SaveChangesAsync();
    }
}
