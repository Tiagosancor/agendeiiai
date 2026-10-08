using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// D7.3 — regra do passado e tolerância de 5 minutos em TODAS as operações que criam ou movem horário, sem depender
/// da hora em que o teste roda. Não há relógio injetável na aplicação; em vez de refatorar, o negócio de teste usa um
/// fuso sem horário de verão em que "agora" cai perto do meio-dia local, com expediente 00:00–23:59: nem a meia-noite
/// nem o fim do turno ficam perto de "agora", então os testes sempre chegam às asserções. As margens usadas (−2 e −7
/// min) ficam a 3 min e 2 min dos 5 min da tolerância, sem sleeps.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class HardeningRelogioTestes : IAsyncLifetime
{
    private static readonly string[] FusosSemHorarioDeVerao =
    [
        "Pacific/Honolulu", "America/Phoenix", "America/Bogota", "America/Sao_Paulo", "UTC", "Africa/Lagos", "Africa/Nairobi",
        "Asia/Dubai", "Asia/Kolkata", "Asia/Dhaka", "Asia/Bangkok", "Asia/Shanghai", "Asia/Tokyo", "Pacific/Noumea",
    ];

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public HardeningRelogioTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public void O_fuso_de_teste_deixa_agora_perto_do_meio_dia_para_qualquer_instante_do_dia()
    {
        // Prova de independência do relógio: para cada um dos 96 instantes de um dia (de 15 em 15 min), o fuso escolhido
        // põe "agora" a no máximo 1h30 do meio-dia local — longe da meia-noite e do fim do expediente 00:00–23:59.
        var inicioDoDia = new DateTimeOffset(2030, 3, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 96; i++)
        {
            var instante = inicioDoDia.AddMinutes(15 * i);
            var local = TimeZoneInfo.ConvertTime(instante, TimeZoneInfo.FindSystemTimeZoneById(FusoPertoDoMeioDia(instante)));
            Math.Abs(local.Hour + local.Minute / 60.0 - 12.0).Should().BeLessThanOrEqualTo(1.5, $"instante {instante:HH:mm}Z");
        }
    }

    // ---------------------------------------------------------------- criar

    [Fact]
    public async Task Lancar_e_iniciar_agora_continua_valendo_em_qualquer_hora_do_dia()
    {
        var c = await ArranjarAsync();
        var profissional = await PlantonistaAsync(c);

        var resposta = await c.Admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            profissional, null, new NovoClienteEncaixe("Balcão", null), [c.Cenario.ServicoId], null, true, false));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("agendamentoId").GetGuid();
        (await Status(id)).Should().Be(StatusAgendamento.EmAtendimento);
    }

    [Fact]
    public async Task Painel_cria_ate_5_minutos_no_passado_recusa_alem_disso_e_o_offset_nao_muda_o_resultado()
    {
        var c = await ArranjarAsync();
        var agora = DateTimeOffset.UtcNow;

        foreach (var (desloc, esperado) in new[] { (TimeSpan.FromMinutes(-7), HttpStatusCode.BadRequest), (TimeSpan.FromHours(-1), HttpStatusCode.BadRequest) })
        {
            foreach (var offset in new[] { TimeSpan.Zero, TimeSpan.FromHours(-3), TimeSpan.FromHours(9), TimeSpan.FromHours(-12) })
            {
                var resposta = await CriarNoPainelAsync(c, await PlantonistaAsync(c), (agora + desloc).ToOffset(offset));
                resposta.StatusCode.Should().Be(esperado, $"{desloc} em offset {offset}");
                (await resposta.Content.ReadAsStringAsync()).Should().Contain("já passou");
            }
        }

        // Dentro da tolerância (e agora, e futuro), em qualquer offset: aceito.
        foreach (var inicio in new[] { agora.AddMinutes(-2), agora, agora.AddHours(1) })
        {
            foreach (var offset in new[] { TimeSpan.Zero, TimeSpan.FromHours(9) })
                (await CriarNoPainelAsync(c, await PlantonistaAsync(c), inicio.ToOffset(offset))).StatusCode.Should().Be(HttpStatusCode.Created, inicio.ToString("o"));
        }
    }

    [Fact]
    public async Task Reserva_publica_segue_a_mesma_tolerancia()
    {
        var c = await ArranjarAsync();
        var agora = DateTimeOffset.UtcNow;

        (await ReservarAsync(c, await PlantonistaAsync(c), agora.AddMinutes(-7))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReservarAsync(c, await PlantonistaAsync(c), agora.AddMinutes(-2).ToOffset(TimeSpan.FromHours(9)))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReservarAsync(c, await PlantonistaAsync(c), agora.AddHours(1))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Encaixe_apenas_encaixar_segue_a_mesma_tolerancia()
    {
        var c = await ArranjarAsync();
        var agora = DateTimeOffset.UtcNow;

        var recusado = await EncaixarAsync(c, await PlantonistaAsync(c), agora.AddMinutes(-7));
        recusado.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await recusado.Content.ReadAsStringAsync()).Should().Contain("já passou");

        (await EncaixarAsync(c, await PlantonistaAsync(c), agora.AddMinutes(-2))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await EncaixarAsync(c, await PlantonistaAsync(c), agora.AddHours(1))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---------------------------------------------------------------- mover

    [Fact]
    public async Task Painel_move_com_a_mesma_tolerancia()
    {
        var c = await ArranjarAsync();
        var agora = DateTimeOffset.UtcNow;
        var (idRecusado, _) = await SemearAsync(c, await PlantonistaAsync(c), agora.AddHours(3));
        var (idAceito, _) = await SemearAsync(c, await PlantonistaAsync(c), agora.AddHours(3));

        var recusado = await c.Admin.PutAsJsonAsync($"/painel/agendamentos/{idRecusado}/mover", new MoverAgendamentoRequisicao(agora.AddMinutes(-7)));
        recusado.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await recusado.Content.ReadAsStringAsync()).Should().Contain("já passou");
        (await c.Admin.PutAsJsonAsync($"/painel/agendamentos/{idAceito}/mover", new MoverAgendamentoRequisicao(agora.AddMinutes(-2))))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Remarcacao_publica_move_com_a_mesma_tolerancia()
    {
        var c = await ArranjarAsync();
        var agora = DateTimeOffset.UtcNow;
        var (idRecusado, tokenRecusado) = await SemearAsync(c, await PlantonistaAsync(c), agora.AddHours(3));
        var (idAceito, tokenAceito) = await SemearAsync(c, await PlantonistaAsync(c), agora.AddHours(3));

        var recusado = await c.Publico.PostAsJsonAsync($"/publico/meus-agendamentos/{tokenRecusado}/remarcar", new { novoInicio = agora.AddMinutes(-7) });
        recusado.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await recusado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("horario_invalido");
        (await Inicio(idRecusado)).Should().BeCloseTo(agora.AddHours(3), TimeSpan.FromSeconds(1));

        (await c.Publico.PostAsJsonAsync($"/publico/meus-agendamentos/{tokenAceito}/remarcar", new { novoInicio = agora.AddMinutes(-2) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Inicio(idAceito)).Should().BeCloseTo(agora.AddMinutes(-2), TimeSpan.FromSeconds(1));
    }

    // ---------------------------------------------------------------- caminho forçado

    [Fact]
    public async Task Caminho_forcado_usa_a_mesma_tolerancia_na_previa_na_criacao_e_na_movimentacao()
    {
        var c = await ArranjarAsync();
        var agora = DateTimeOffset.UtcNow;
        var profissional = await PlantonistaAsync(c);

        var previaPassado = await PreverAsync(c, profissional, agora.AddMinutes(-7));
        previaPassado.Impedimento.Should().Contain("já passou");
        (await PreverAsync(c, profissional, agora.AddMinutes(-2))).Impedimento.Should().BeNull();

        var recusado = await c.Admin.PostAsJsonAsync("/painel/agendamentos/forcados",
            new CriarAgendamentoForcado(profissional, c.Cenario.ClienteId, [c.Cenario.ServicoId], agora.AddMinutes(-7), null, "Motivo"));
        recusado.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await recusado.Content.ReadAsStringAsync()).Should().Contain("já passou");
        (await c.Admin.PostAsJsonAsync("/painel/agendamentos/forcados",
            new CriarAgendamentoForcado(profissional, c.Cenario.ClienteId, [c.Cenario.ServicoId], agora.AddMinutes(-2), null, "Motivo")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var (idRecusado, _) = await SemearAsync(c, await PlantonistaAsync(c), agora.AddHours(3));
        var (idAceito, _) = await SemearAsync(c, await PlantonistaAsync(c), agora.AddHours(3));
        (await c.Admin.PutAsJsonAsync($"/painel/agendamentos/forcados/{idRecusado}/mover", new MoverAgendamentoForcado(agora.AddMinutes(-7), "Motivo")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.Admin.PutAsJsonAsync($"/painel/agendamentos/forcados/{idAceito}/mover", new MoverAgendamentoForcado(agora.AddMinutes(-2), "Motivo")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---------------------------------------------------------------- apoio

    private sealed record Contexto(HttpClient Admin, HttpClient Publico, Guid NegocioId, SemeadorDeAgenda.CenarioDeAgenda Cenario);

    private async Task<Contexto> ArranjarAsync()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        var fuso = FusoPertoDoMeioDia(DateTimeOffset.UtcNow);
        await _fabrica.NoBancoAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE negocios SET fuso = {0} WHERE id = {1}", fuso, negocioId));

        var negocio = await _fabrica.NoBancoAsync(db => db.Negocios.IgnoreQueryFilters().SingleAsync(n => n.Id == negocioId));
        var horaLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso));
        Math.Abs(horaLocal.Hour + horaLocal.Minute / 60.0 - 12.0).Should().BeLessThanOrEqualTo(1.6, "o cenário só é determinístico longe da meia-noite");
        var publico = _fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{negocio.Slug.Valor}.{DominioDeTeste.Valor}/"),
        });
        return new Contexto(admin, publico, negocioId, cenario);
    }

    /// <summary>O fuso (sem horário de verão) em que agora está mais perto de 12:00 — no pior caso a ±1h30 do meio-dia.</summary>
    internal static string FusoPertoDoMeioDia(DateTimeOffset agora) =>
        FusosSemHorarioDeVerao.OrderBy(id =>
        {
            var local = TimeZoneInfo.ConvertTime(agora, TimeZoneInfo.FindSystemTimeZoneById(id));
            return Math.Abs(local.Hour + local.Minute / 60.0 - 12.0);
        }).First();

    /// <summary>Profissional novo com expediente 00:00–23:59 todos os dias — um por operação, para os horários não se sobreporem.</summary>
    private async Task<Guid> PlantonistaAsync(Contexto c)
    {
        var profissional = await _fabrica.CriarProfissionalAsync(c.NegocioId, $"Plantonista {Guid.NewGuid():N}"[..20]);
        await _fabrica.NoBancoAsync(async db =>
        {
            foreach (var dia in Enum.GetValues<DiaSemana>())
                db.HorariosTrabalho.Add(HorarioTrabalho.Criar(c.NegocioId, profissional, dia, new TimeOnly(0, 0), new TimeOnly(23, 59)));
            return await db.SaveChangesAsync();
        });
        return profissional;
    }

    private Task<HttpResponseMessage> CriarNoPainelAsync(Contexto c, Guid profissional, DateTimeOffset inicio) =>
        c.Admin.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(profissional, c.Cenario.ClienteId, [c.Cenario.ServicoId], inicio));

    private Task<HttpResponseMessage> ReservarAsync(Contexto c, Guid profissional, DateTimeOffset inicio) =>
        c.Publico.PostAsJsonAsync("/publico/reservas", new { profissionalId = profissional, servicoIds = new[] { c.Cenario.ServicoId }, inicio });

    private Task<HttpResponseMessage> EncaixarAsync(Contexto c, Guid profissional, DateTimeOffset inicio) =>
        c.Admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            profissional, null, new NovoClienteEncaixe($"Balcão {Guid.NewGuid():N}"[..14], null), [c.Cenario.ServicoId], inicio, false, false));

    private async Task<PreviaForcar> PreverAsync(Contexto c, Guid profissional, DateTimeOffset inicio)
    {
        var resposta = await c.Admin.PostAsJsonAsync("/painel/agendamentos/forcados/previa", new ConsultaForcar(profissional, [c.Cenario.ServicoId], inicio));
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resposta.Content.ReadFromJsonAsync<PreviaForcar>())!;
    }

    private async Task<(Guid Id, string Token)> SemearAsync(Contexto c, Guid profissional, DateTimeOffset inicio)
    {
        var id = await _fabrica.SemearAgendamentoAsync(c.NegocioId, profissional, inicio, servicoId: c.Cenario.ServicoId);
        return (id, _fabrica.Services.GetRequiredService<IServicoTokenPublico>().GerarTokenAgendamento(c.NegocioId, id));
    }

    private Task<StatusAgendamento> Status(Guid id) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().Where(a => a.Id == id).Select(a => a.Status).SingleAsync());

    private Task<DateTimeOffset> Inicio(Guid id) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().Where(a => a.Id == id).Select(a => a.Inicio).SingleAsync());
}
