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
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>Forçar agendamento (seção 7 e 8.2, item 10 da seção 14).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ForcarAgendamentoTestes : IAsyncLifetime
{
    private const string Rota = "/painel/agendamentos/forcados";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public ForcarAgendamentoTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Aviso_lista_as_regras_quebradas_e_o_forcado_grava_motivo_autor_regras_e_auditoria()
    {
        var (admin, negocioId, usuarioId, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegunda();

        // Cliente de Teste às 11:30 e uma folga das 12:10 às 12:20; forçar 11:45–12:15 cruza os dois e o almoço.
        (await admin.PostAsJsonAsync("/painel/agendamentos",
            new CriarAgendamento(cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], Local(segunda, 11, 30))))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await _fabrica.NoBancoAsync(async db =>
        {
            db.BloqueiosAgenda.Add(BloqueioAgenda.Criar(negocioId, cenario.ProfissionalId, Local(segunda, 12, 10), Local(segunda, 12, 20), "Dentista"));
            return await db.SaveChangesAsync();
        });

        var previa = await PreverAsync(admin, cenario, Local(segunda, 11, 45));
        previa.Impedimento.Should().BeNull();
        previa.Regras.Should().HaveCount(3);
        previa.Regras.Should().Contain(r => r.Contains("almoço") && r.Contains("12:00–13:00"));
        previa.Regras.Should().Contain(r => r.Contains("folga/bloqueio") && r.Contains("Dentista"));
        previa.Regras.Should().Contain("Sobrepõe o agendamento de Cliente de Teste às 11:30");

        var criado = await admin.PostAsJsonAsync(Rota, Forcado(cenario, Local(segunda, 11, 45), "Cliente VIP, só pode nesse horário"));
        criado.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await criado.Content.ReadFromJsonAsync<Guid>();

        var agendamento = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == id));
        agendamento.Forcado.Should().BeTrue();
        agendamento.ForcadoMotivo.Should().Be("Cliente VIP, só pode nesse horário");
        agendamento.ForcadoPorUsuarioId.Should().Be(usuarioId);
        agendamento.ForcadoEm.Should().NotBeNull();
        // Gravado sem o nome do outro cliente (sobrevive à anonimização dele).
        agendamento.ForcadoRegras.Should().Contain("Sobrepõe outro agendamento às 11:30").And.NotContain("Cliente de Teste");

        var auditoria = await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .SingleAsync(l => l.EntidadeId == id && l.Acao == AcoesAuditoria.ForcarAgendamento));
        auditoria.Detalhes.Should().Contain("Cliente VIP").And.Contain("almoço");

        var agenda = await admin.GetFromJsonAsync<List<AgendamentoResumo>>(
            $"/painel/agenda?profissionalId={cenario.ProfissionalId}&data={segunda:yyyy-MM-dd}");
        var naAgenda = agenda!.Single(a => a.Id == id);
        naAgenda.Forcado.Should().BeTrue();
        naAgenda.ForcadoMotivo.Should().Be("Cliente VIP, só pode nesse horário");
        naAgenda.ForcadoPor.Should().Be("Usuário Teste");
        naAgenda.ForcadoRegras.Should().HaveCount(3);
        agenda!.Single(a => a.Id != id).Forcado.Should().BeFalse();
    }

    [Fact]
    public async Task Folga_da_semana_e_fora_do_expediente_aparecem_no_aviso()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegunda();

        (await PreverAsync(admin, cenario, Local(segunda.AddDays(-1), 10, 0))).Regras
            .Should().ContainSingle(r => r.Contains("não trabalha nesse dia"));
        (await PreverAsync(admin, cenario, Local(segunda, 18, 0))).Regras
            .Should().Equal("Fora do expediente do profissional (09:00–12:00, 13:00–18:00)");
        (await PreverAsync(admin, cenario, Local(segunda, 10, 0))).Regras.Should().BeEmpty();

        // Nada quebrado: grava como agendamento comum, sem marca de forçado.
        var comum = await admin.PostAsJsonAsync(Rota, Forcado(cenario, Local(segunda, 10, 0), "Tanto faz"));
        comum.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await comum.Content.ReadFromJsonAsync<Guid>();
        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == id))).Forcado.Should().BeFalse();
    }

    [Fact]
    public async Task Sem_a_permissao_recebe_403_e_sem_motivo_e_recusado()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var inicio = Local(ProximaSegunda(), 19, 0);

        // A Recepcionista padrão gerencia a agenda e lança encaixe, mas não força.
        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista,
            permissoesExtras: Usuario.PermissoesPadrao(Perfil.Recepcionista).ToArray());
        (await recepcao.PostAsJsonAsync(Rota, Forcado(cenario, inicio, "Motivo"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.PostAsJsonAsync($"{Rota}/previa", new ConsultaForcar(cenario.ProfissionalId, [cenario.ServicoId], inicio)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(cenario.ProfissionalId, cenario.ClienteId, null,
            [cenario.ServicoId], inicio, false, false, MotivoForcar: "Motivo"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (var motivo in new string?[] { null, "", "   " })
        {
            var semMotivo = await admin.PostAsJsonAsync(Rota, Forcado(cenario, inicio, motivo));
            semMotivo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await semMotivo.Content.ReadAsStringAsync()).Should().Contain("Informe o motivo");
        }

        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AnyAsync(a => a.NegocioId == negocioId))).Should().BeFalse();
    }

    [Fact]
    public async Task Profissional_inativo_data_no_passado_e_negocio_suspenso_nunca_sao_forcados()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        var passado = await admin.PostAsJsonAsync(Rota, Forcado(cenario, DateTimeOffset.UtcNow.AddHours(-2), "Esqueci de lançar"));
        passado.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await passado.Content.ReadAsStringAsync()).Should().Contain("já passou");
        (await PreverAsync(admin, cenario, DateTimeOffset.UtcNow.AddHours(-2))).Impedimento.Should().Contain("já passou");

        await _fabrica.NoBancoAsync(async db =>
        {
            (await db.Profissionais.IgnoreQueryFilters().SingleAsync(p => p.Id == cenario.ProfissionalId)).Desativar();
            return await db.SaveChangesAsync();
        });
        var inativo = await admin.PostAsJsonAsync(Rota, Forcado(cenario, Local(ProximaSegunda(), 19, 0), "Motivo"));
        inativo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await inativo.Content.ReadAsStringAsync()).Should().Contain("inativo");

        await _fabrica.NoBancoAsync(async db =>
        {
            var plano = await db.Planos.FirstAsync();
            var assinatura = ServicoAssinatura.IniciarTeste(negocioId, plano, Periodicidade.Mensal, "teste", DateTimeOffset.UtcNow.AddDays(-45));
            ServicoAssinatura.AtualizarPorTempo(assinatura, DateTimeOffset.UtcNow);
            db.Assinaturas.Add(assinatura);
            return await db.SaveChangesAsync();
        });
        (await admin.PostAsJsonAsync(Rota, Forcado(cenario, Local(ProximaSegunda(), 19, 0), "Motivo")))
            .StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AnyAsync(a => a.NegocioId == negocioId))).Should().BeFalse();
    }

    [Fact]
    public async Task Forcado_ocupa_o_horario_no_painel_e_no_link_publico_que_nao_aceita_nem_grava_forcado()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegunda();
        using var publico = await ClientePublicoAsync(negocioId);
        var urlLivresPublico = $"/publico/horarios-livres?data={segunda:yyyy-MM-dd}&duracaoMinutos=30&servicoIds={cenario.ServicoId}&profissionalId={cenario.ProfissionalId}";
        var urlLivresPainel = $"/painel/profissionais/{cenario.ProfissionalId}/horarios-livres?data={segunda:yyyy-MM-dd}&duracaoMinutos=30";

        (await admin.GetFromJsonAsync<List<DateTimeOffset>>(urlLivresPainel))!.Should().Contain(Local(segunda, 17, 30));

        // 17:45–18:15: passa do expediente, então fica forçado — e ocupa 17:30 e 17:45 para todo o resto.
        (await admin.PostAsJsonAsync(Rota, Forcado(cenario, Local(segunda, 17, 45), "Último cliente do dia")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await admin.GetFromJsonAsync<List<DateTimeOffset>>(urlLivresPainel))!
            .Should().NotContain([Local(segunda, 17, 30), Local(segunda, 17, 45)]);
        (await publico.GetFromJsonAsync<List<HorarioLivrePublicoDto>>(urlLivresPublico))!.Select(h => h.Inicio)
            .Should().NotContain([Local(segunda, 17, 30), Local(segunda, 17, 45)]).And.Contain(Local(segunda, 17, 0));

        // O link público ignora "forcado"/"motivo" no corpo: por cima do forçado, 409; num horário livre, reserva comum.
        var porCima = await publico.PostAsJsonAsync("/publico/reservas", new
        {
            profissionalId = cenario.ProfissionalId, servicoIds = new[] { cenario.ServicoId }, inicio = Local(segunda, 17, 30),
            forcado = true, motivo = "Me deixa passar", motivoForcar = "Me deixa passar",
        });
        porCima.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var livre = await publico.PostAsJsonAsync("/publico/reservas", new
        {
            profissionalId = cenario.ProfissionalId, servicoIds = new[] { cenario.ServicoId }, inicio = Local(segunda, 10, 0),
            forcado = true, motivoForcar = "Me deixa passar",
        });
        livre.StatusCode.Should().Be(HttpStatusCode.Created);

        var agendamentos = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().Where(a => a.NegocioId == negocioId).ToListAsync());
        agendamentos.Should().HaveCount(2);
        agendamentos.Should().ContainSingle(a => a.Forcado).Which.Inicio.Should().Be(Local(segunda, 17, 45));
    }

    [Fact]
    public async Task Cem_requisicoes_simultaneas_por_cima_de_um_forcado_nao_criam_nenhuma_sobreposicao()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegunda();

        (await admin.PostAsJsonAsync(Rota, Forcado(cenario, Local(segunda, 17, 45), "Último cliente do dia")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var dados = new CriarAgendamento(cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], Local(segunda, 17, 30));
        var respostas = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => admin.PostAsJsonAsync("/painel/agendamentos", dados)));

        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().CountAsync(a => a.NegocioId == negocioId))).Should().Be(1);

        foreach (var resposta in respostas)
            resposta.Dispose();
    }

    [Fact]
    public async Task Remarcar_pelo_link_um_agendamento_forcado_aplica_as_regras_normais()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegunda();
        using var publico = await ClientePublicoAsync(negocioId);

        var id = await (await admin.PostAsJsonAsync(Rota, Forcado(cenario, Local(segunda, 18, 30), "Fora do horário, combinado")))
            .Content.ReadFromJsonAsync<Guid>();
        var token = _fabrica.Services.GetRequiredService<IServicoTokenPublico>().GerarTokenAgendamento(negocioId, id);

        (await publico.PostAsJsonAsync($"/publico/meus-agendamentos/{token}/remarcar", new { novoInicio = Local(segunda, 19, 0) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await publico.PostAsJsonAsync($"/publico/meus-agendamentos/{token}/remarcar", new { novoInicio = Local(segunda, 10, 0) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var remarcado = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == id));
        remarcado.Inicio.Should().Be(Local(segunda, 10, 0));
        remarcado.Forcado.Should().BeFalse();
        remarcado.ForcadoMotivo.Should().BeNull();
    }

    [Fact]
    public async Task Encaixe_e_remarcacao_do_painel_tambem_podem_ser_forcados_e_a_remarcacao_normal_respeita_o_forcado()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegunda();

        var encaixe = await admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(cenario.ProfissionalId, null,
            new NovoClienteEncaixe("Balcão", null), [cenario.ServicoId], Local(segunda, 17, 45), false, false, MotivoForcar: "Chegou atrasado"));
        encaixe.StatusCode.Should().Be(HttpStatusCode.Created);
        var encaixeId = (await encaixe.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("agendamentoId").GetGuid();
        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == encaixeId))).Forcado.Should().BeTrue();

        var comum = await (await admin.PostAsJsonAsync("/painel/agendamentos",
            new CriarAgendamento(cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], Local(segunda, 10, 0)))).Content.ReadFromJsonAsync<Guid>();

        (await admin.PutAsJsonAsync($"/painel/agendamentos/{comum}/mover", new MoverAgendamentoRequisicao(Local(segunda, 17, 30))))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync($"{Rota}/{comum}/mover", new MoverAgendamentoForcado(Local(segunda, 17, 30), null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync($"{Rota}/{comum}/mover", new MoverAgendamentoForcado(Local(segunda, 17, 30), "Troca combinada")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var movido = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == comum));
        movido.Inicio.Should().Be(Local(segunda, 17, 30));
        movido.Forcado.Should().BeTrue();
        movido.ForcadoRegras.Should().Contain("Sobrepõe outro agendamento às 17:45");
        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .SingleAsync(l => l.EntidadeId == comum && l.Acao == AcoesAuditoria.ForcarAgendamento))).Detalhes.Should().StartWith("Movido de");
    }

    // ---------------------------------------------------------------- apoio

    private sealed record HorarioLivrePublicoDto(DateTimeOffset Inicio, Guid ProfissionalId);

    private static CriarAgendamentoForcado Forcado(SemeadorDeAgenda.CenarioDeAgenda cenario, DateTimeOffset inicio, string? motivo) =>
        new(cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio, null, motivo);

    private static async Task<PreviaForcar> PreverAsync(HttpClient cliente, SemeadorDeAgenda.CenarioDeAgenda cenario, DateTimeOffset inicio)
    {
        var resposta = await cliente.PostAsJsonAsync($"{Rota}/previa", new ConsultaForcar(cenario.ProfissionalId, [cenario.ServicoId], inicio));
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resposta.Content.ReadFromJsonAsync<PreviaForcar>())!;
    }

    /// <summary>Uma segunda-feira daqui a pelo menos 3 dias (o expediente do cenário padrão é de segunda a sexta).</summary>
    private static DateOnly ProximaSegunda()
    {
        var dia = SemeadorDeComissoes.Dia(3);
        while (dia.DayOfWeek != DayOfWeek.Monday)
            dia = dia.AddDays(1);
        return dia;
    }

    private static DateTimeOffset Local(DateOnly dia, int hora, int minuto) => SemeadorDeComissoes.Local(dia, hora, minuto);

    private async Task<HttpClient> ClientePublicoAsync(Guid negocioId)
    {
        var negocio = await _fabrica.NoBancoAsync(db => db.Negocios.IgnoreQueryFilters().SingleAsync(n => n.Id == negocioId));
        return _fabrica.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{negocio.Slug.Valor}.{DominioDeTeste.Valor}/"),
        });
    }
}
