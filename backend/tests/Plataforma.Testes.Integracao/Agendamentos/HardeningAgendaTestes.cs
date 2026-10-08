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
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// D7.2 — integridade da agenda: nunca criar nem mover para o passado (H2) e o intervalo inteiro [início, fim)
/// precisa caber num turno do dia (H5). O que a disponibilidade não oferece, o POST normal também não aceita.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class HardeningAgendaTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public HardeningAgendaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    // ---------------------------------------------------------------- H2 — passado

    [Fact]
    public async Task Painel_nao_cria_no_passado_aceita_o_futuro_e_o_offset_nao_muda_o_resultado()
    {
        var c = await ArranjarAsync();
        var ontem = SemeadorDeComissoes.Local(SemeadorDeComissoes.Dia(-1), 10, 0);
        var haDezMinutos = DateTimeOffset.UtcNow.AddMinutes(-10);

        foreach (var passado in new[] { ontem, ontem.ToOffset(TimeSpan.FromHours(-3)), ontem.ToOffset(TimeSpan.FromHours(9)), haDezMinutos })
        {
            var resposta = await c.Admin.PostAsJsonAsync("/painel/agendamentos",
                new CriarAgendamento(c.Cenario.ProfissionalId, c.Cenario.ClienteId, [c.Cenario.ServicoId], passado));
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, passado.ToString("o"));
            (await resposta.Content.ReadAsStringAsync()).Should().Contain("já passou");
        }

        var futuro = await c.Admin.PostAsJsonAsync("/painel/agendamentos",
            new CriarAgendamento(c.Cenario.ProfissionalId, c.Cenario.ClienteId, [c.Cenario.ServicoId], Local(c, 10, 0)));
        futuro.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Contar(c)).Should().Be(1);
    }

    [Fact]
    public async Task Reserva_publica_no_passado_e_recusada_e_o_futuro_continua_aceito()
    {
        var c = await ArranjarAsync();
        var ontem = SemeadorDeComissoes.Local(SemeadorDeComissoes.Dia(-1), 10, 0);

        foreach (var passado in new[] { ontem, ontem.ToOffset(TimeSpan.FromHours(-3)) })
        {
            var resposta = await Reservar(c, passado);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await resposta.Content.ReadAsStringAsync()).Should().Contain("já passou");
        }

        (await Reservar(c, Local(c, 10, 0))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Contar(c)).Should().Be(1);
    }

    [Fact]
    public async Task Remarcacao_publica_manual_para_o_passado_e_recusada_mesmo_sem_vir_da_disponibilidade()
    {
        var c = await ArranjarAsync();
        var (id, token) = await SemearAsync(c, Local(c, 10, 0));
        var ontem = SemeadorDeComissoes.Local(SemeadorDeComissoes.Dia(-1), 10, 0);

        foreach (var passado in new[] { ontem, ontem.ToOffset(TimeSpan.FromHours(-3)) })
        {
            var resposta = await c.Publico.PostAsJsonAsync($"/publico/meus-agendamentos/{token}/remarcar", new { novoInicio = passado });
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var json = await resposta.Content.ReadFromJsonAsync<JsonElement>();
            json.GetProperty("codigo").GetString().Should().Be("horario_invalido");
            json.GetProperty("title").GetString().Should().Contain("já passou");
        }

        (await InicioNoBanco(id)).Should().Be(Local(c, 10, 0));

        // A lista de horários nunca ofereceu nada do passado: oferecer e aceitar concordam.
        var oferta = await c.Publico.GetFromJsonAsync<JsonElement>(
            $"/publico/meus-agendamentos/{token}/horarios-livres?data={SemeadorDeComissoes.Dia(-1):yyyy-MM-dd}");
        oferta.GetProperty("horarios").GetArrayLength().Should().Be(0);

        (await c.Publico.PostAsJsonAsync($"/publico/meus-agendamentos/{token}/remarcar", new { novoInicio = Local(c, 14, 0) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await InicioNoBanco(id)).Should().Be(Local(c, 14, 0));
    }

    [Fact]
    public async Task Painel_nao_remarca_para_o_passado()
    {
        var c = await ArranjarAsync();
        var (id, _) = await SemearAsync(c, Local(c, 10, 0));

        var resposta = await c.Admin.PutAsJsonAsync($"/painel/agendamentos/{id}/mover",
            new MoverAgendamentoRequisicao(SemeadorDeComissoes.Local(SemeadorDeComissoes.Dia(-1), 10, 0)));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("já passou");
        (await InicioNoBanco(id)).Should().Be(Local(c, 10, 0));
    }

    [Fact]
    public async Task Encaixe_lancar_e_iniciar_agora_continua_valendo_e_encaixe_no_passado_nao()
    {
        var c = await ArranjarAsync();
        var agora = SemeadorDeComissoes.Fuso is { } fuso ? TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, fuso) : DateTimeOffset.UtcNow;
        if (agora.Hour == 23 && agora.Minute >= 20)
            return; // um serviço de 30 min iniciado agora atravessaria a meia-noite: a regra de H5 o recusa de propósito.

        var profissional = await ComTurnoAsync(c.NegocioId, "Plantonista", new TimeOnly(0, 0), new TimeOnly(23, 59), todosOsDias: true);

        var iniciar = await c.Admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            profissional, null, new NovoClienteEncaixe("Balcão", null), [c.Cenario.ServicoId], null, true, false));
        iniciar.StatusCode.Should().Be(HttpStatusCode.Created);
        var idIniciado = (await iniciar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("agendamentoId").GetGuid();
        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().Where(a => a.Id == idIniciado).Select(a => a.Status).SingleAsync()))
            .Should().Be(StatusAgendamento.EmAtendimento);

        var noPassado = await c.Admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            profissional, null, new NovoClienteEncaixe("Balcão 2", null), [c.Cenario.ServicoId],
            SemeadorDeComissoes.Local(SemeadorDeComissoes.Dia(-1), 10, 0), false, false));
        noPassado.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noPassado.Content.ReadAsStringAsync()).Should().Contain("já passou");
    }

    // ---------------------------------------------------------------- H5 — intervalo inteiro no expediente

    [Fact]
    public async Task Criacao_recusa_o_que_termina_fora_do_expediente_inclusive_no_dia_seguinte()
    {
        var c = await ArranjarAsync();

        // Expediente do cenário: 09:00–12:00 e 13:00–18:00 (servico de 30 min).
        var invalidos = new[] { (8, 45), (11, 45), (12, 0), (17, 45), (18, 0), (23, 30), (23, 45) };
        foreach (var (hora, minuto) in invalidos)
        {
            var painel = await c.Admin.PostAsJsonAsync("/painel/agendamentos",
                new CriarAgendamento(c.Cenario.ProfissionalId, c.Cenario.ClienteId, [c.Cenario.ServicoId], Local(c, hora, minuto)));
            painel.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"painel {hora}:{minuto:00}");

            var publico = await Reservar(c, Local(c, hora, minuto));
            publico.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"público {hora}:{minuto:00}");
            (await publico.Content.ReadAsStringAsync()).Should().Contain("Fora do expediente");
        }

        (await Contar(c)).Should().Be(0);
        (await Reservar(c, Local(c, 17, 30))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Turno_ate_23_59_nao_aceita_o_que_atravessa_a_meia_noite()
    {
        var c = await ArranjarAsync();
        var noite = await ComTurnoAsync(c.NegocioId, "Noturno", new TimeOnly(0, 0), new TimeOnly(23, 59), todosOsDias: true);

        // Início 23:50 + 30 min = 00:20 do dia seguinte (caso de referência do D7.1).
        var cruza = await Reservar(c, Local(c, 23, 50), noite);
        cruza.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await cruza.Content.ReadAsStringAsync()).Should().Contain("Fora do expediente");

        (await Reservar(c, Local(c, 23, 20), noite)).StatusCode.Should().Be(HttpStatusCode.Created); // termina 23:50
    }

    [Fact]
    public async Task Varios_servicos_somam_a_duracao_ao_validar_o_intervalo()
    {
        var c = await ArranjarAsync();
        var segundo = await CriarServicoAsync(c, "Barba", 30);

        // 2 × 30 min = 60 min.
        (await Reservar(c, Local(c, 17, 15), servicos: [c.Cenario.ServicoId, segundo])).StatusCode.Should().Be(HttpStatusCode.BadRequest); // 18:15
        (await Reservar(c, Local(c, 23, 30), servicos: [c.Cenario.ServicoId, segundo])).StatusCode.Should().Be(HttpStatusCode.BadRequest); // 00:30
        (await Reservar(c, Local(c, 17, 0), servicos: [c.Cenario.ServicoId, segundo])).StatusCode.Should().Be(HttpStatusCode.Created);   // 18:00
    }

    [Fact]
    public async Task Remarcacao_publica_e_do_painel_recusam_o_que_atravessa_o_dia_e_mantem_o_horario_antigo()
    {
        var c = await ArranjarAsync();
        var (id, token) = await SemearAsync(c, Local(c, 10, 0));

        foreach (var (hora, minuto) in new[] { (23, 45), (17, 45), (8, 45) })
        {
            var publico = await c.Publico.PostAsJsonAsync($"/publico/meus-agendamentos/{token}/remarcar", new { novoInicio = Local(c, hora, minuto) });
            publico.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"público {hora}:{minuto:00}");
            (await publico.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("horario_invalido");

            (await c.Admin.PutAsJsonAsync($"/painel/agendamentos/{id}/mover", new MoverAgendamentoRequisicao(Local(c, hora, minuto))))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest, $"painel {hora}:{minuto:00}");
        }

        (await InicioNoBanco(id)).Should().Be(Local(c, 10, 0));
        (await c.Publico.PostAsJsonAsync($"/publico/meus-agendamentos/{token}/remarcar", new { novoInicio = Local(c, 17, 30) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Transferencia_recusa_quando_o_intervalo_nao_cabe_no_expediente_do_outro_profissional()
    {
        var c = await ArranjarAsync();
        var origem = await ComTurnoAsync(c.NegocioId, "Origem", new TimeOnly(0, 0), new TimeOnly(23, 59), todosOsDias: true);
        // Agendamento que já atravessa a meia-noite (23:45–00:15 do dia seguinte), semeado direto: só a transferência o testa.
        var atravessa = await _fabrica.SemearAgendamentoAsync(c.NegocioId, origem, Local(c, 23, 45), servicoId: c.Cenario.ServicoId);
        var cabe = await _fabrica.SemearAgendamentoAsync(c.NegocioId, origem, Local(c, 10, 0), servicoId: c.Cenario.ServicoId);
        // A transferência exige que o destino execute o serviço (regra existente): vincula antes.
        await _fabrica.NoBancoAsync(async db =>
        {
            db.ProfissionalServicos.Add(ProfissionalServico.Criar(c.NegocioId, c.Cenario.ProfissionalId, c.Cenario.ServicoId));
            return await db.SaveChangesAsync();
        });

        var recusada = await c.Admin.PostAsJsonAsync(
            $"/painel/profissionais/{origem}/agendamentos-futuros/{atravessa}/transferir", new TransferirAgendamentoRequisicao(c.Cenario.ProfissionalId));
        recusada.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await recusada.Content.ReadAsStringAsync()).Should().Contain("fora do expediente");

        var aceita = await c.Admin.PostAsJsonAsync(
            $"/painel/profissionais/{origem}/agendamentos-futuros/{cabe}/transferir", new TransferirAgendamentoRequisicao(c.Cenario.ProfissionalId));
        aceita.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var profissionais = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters()
            .Where(a => a.Id == atravessa || a.Id == cabe).ToDictionaryAsync(a => a.Id, a => a.ProfissionalId));
        profissionais[atravessa].Should().Be(origem);
        profissionais[cabe].Should().Be(c.Cenario.ProfissionalId);
    }

    [Fact]
    public async Task Caminho_forcado_continua_apontando_a_travessia_como_regra_quebrada_e_grava_forcado()
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 23, 45);

        var previa = await c.Admin.PostAsJsonAsync("/painel/agendamentos/forcados/previa",
            new ConsultaForcar(c.Cenario.ProfissionalId, [c.Cenario.ServicoId], inicio));
        previa.StatusCode.Should().Be(HttpStatusCode.OK);
        (await previa.Content.ReadFromJsonAsync<PreviaForcar>())!.Regras.Should().Contain(r => r.Contains("Fora do expediente"));

        var criado = await c.Admin.PostAsJsonAsync("/painel/agendamentos/forcados",
            new CriarAgendamentoForcado(c.Cenario.ProfissionalId, c.Cenario.ClienteId, [c.Cenario.ServicoId], inicio, null, "Cliente pediu"));
        criado.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await criado.Content.ReadFromJsonAsync<Guid>();
        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == id))).Forcado.Should().BeTrue();
    }

    // ---------------------------------------------------------------- oferece × aceita

    [Fact]
    public async Task O_que_o_endpoint_de_horarios_oferece_e_exatamente_o_que_o_POST_normal_aceita()
    {
        var c = await ArranjarAsync();
        var url = $"/publico/horarios-livres?data={c.Segunda:yyyy-MM-dd}&duracaoMinutos=30&servicoIds={c.Cenario.ServicoId}&profissionalId={c.Cenario.ProfissionalId}";
        var oferecidos = (await c.Publico.GetFromJsonAsync<List<HorarioPublico>>(url))!.Select(h => h.Inicio).ToHashSet();

        // Candidatos que não se sobrepõem entre os aceitos (cada reserva aceita ocupa seu horário).
        var candidatos = new[] { (8, 45), (9, 0), (11, 30), (11, 45), (12, 0), (13, 0), (17, 30), (17, 45), (18, 0), (23, 30), (23, 45) };
        foreach (var (hora, minuto) in candidatos)
        {
            var inicio = Local(c, hora, minuto);
            var resposta = await Reservar(c, inicio);
            var aceito = resposta.StatusCode == HttpStatusCode.Created;
            aceito.Should().Be(oferecidos.Contains(inicio), $"{hora}:{minuto:00} — oferecer e aceitar precisam coincidir");
        }

        // E o passado: nunca oferecido, nunca aceito.
        var ontem = SemeadorDeComissoes.Dia(-1);
        (await c.Publico.GetFromJsonAsync<List<HorarioPublico>>(
            $"/publico/horarios-livres?data={ontem:yyyy-MM-dd}&duracaoMinutos=30&servicoIds={c.Cenario.ServicoId}&profissionalId={c.Cenario.ProfissionalId}"))!
            .Should().BeEmpty();
        (await Reservar(c, SemeadorDeComissoes.Local(ontem, 10, 0))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- apoio

    private sealed record Contexto(HttpClient Admin, HttpClient Publico, Guid NegocioId, SemeadorDeAgenda.CenarioDeAgenda Cenario, DateOnly Segunda);

    private sealed record HorarioPublico(DateTimeOffset Inicio, Guid ProfissionalId);

    private async Task<Contexto> ArranjarAsync()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var negocio = await _fabrica.NoBancoAsync(db => db.Negocios.IgnoreQueryFilters().SingleAsync(n => n.Id == negocioId));
        var publico = _fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{negocio.Slug.Valor}.{DominioDeTeste.Valor}/"),
        });
        return new Contexto(admin, publico, negocioId, cenario, ProximaSegunda());
    }

    private static DateTimeOffset Local(Contexto c, int hora, int minuto) => SemeadorDeComissoes.Local(c.Segunda, hora, minuto);

    private Task<HttpResponseMessage> Reservar(Contexto c, DateTimeOffset inicio, Guid? profissional = null, Guid[]? servicos = null) =>
        c.Publico.PostAsJsonAsync("/publico/reservas", new
        {
            profissionalId = profissional ?? c.Cenario.ProfissionalId,
            servicoIds = servicos ?? [c.Cenario.ServicoId],
            inicio,
        });

    private async Task<(Guid Id, string Token)> SemearAsync(Contexto c, DateTimeOffset inicio)
    {
        var id = await _fabrica.SemearAgendamentoAsync(c.NegocioId, c.Cenario.ProfissionalId, inicio, servicoId: c.Cenario.ServicoId);
        return (id, _fabrica.Services.GetRequiredService<IServicoTokenPublico>().GerarTokenAgendamento(c.NegocioId, id));
    }

    private Task<DateTimeOffset> InicioNoBanco(Guid id) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().Where(a => a.Id == id).Select(a => a.Inicio).SingleAsync());

    private Task<int> Contar(Contexto c) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().CountAsync(a => a.NegocioId == c.NegocioId));

    private async Task<Guid> ComTurnoAsync(Guid negocioId, string nome, TimeOnly inicio, TimeOnly fim, bool todosOsDias)
    {
        var profissional = await _fabrica.CriarProfissionalAsync(negocioId, nome);
        await _fabrica.NoBancoAsync(async db =>
        {
            foreach (var dia in Enum.GetValues<DiaSemana>())
                db.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocioId, profissional, dia, inicio, fim));
            return await db.SaveChangesAsync();
        });
        return profissional;
    }

    private async Task<Guid> CriarServicoAsync(Contexto c, string nome, int duracaoMinutos)
    {
        var categoria = await (await c.Admin.PostAsJsonAsync("/painel/categorias", new { nome = $"Cat {nome}" })).Content.ReadFromJsonAsync<Guid>();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/servicos", new { categoriaId = categoria, nome, preco = 30m, duracaoMinutos });
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    private static DateOnly ProximaSegunda()
    {
        var dia = SemeadorDeComissoes.Dia(3);
        while (dia.DayOfWeek != DayOfWeek.Monday)
            dia = dia.AddDays(1);
        return dia;
    }
}
