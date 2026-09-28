using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Comissoes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.Infraestrutura.SemeadorDeComissoes;

namespace Plataforma.Testes.Integracao.Comissoes;

/// <summary>Fechamento de comissões por quinzena (seção 7, item 7 da seção 14).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class QuinzenasTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    // Um mês inteiro no passado: 1ª quinzena (1–15) e 2ª (16–fim).
    private static readonly DateOnly Dia1 = new(Dia(-60).Year, Dia(-60).Month, 1);
    private static readonly DateOnly Dia15 = Dia1.AddDays(14);
    private static readonly DateOnly Dia16 = Dia1.AddDays(15);
    private static readonly DateOnly FimDoMes = Dia1.AddMonths(1).AddDays(-1);

    public QuinzenasTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Quinzenas_que_se_sobrepoem_sao_recusadas_pelo_banco_inclusive_ao_mesmo_tempo()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        (await CriarAsync(admin, Dia1, Dia15)).StatusCode.Should().Be(HttpStatusCode.Created);
        var sobreposta = await CriarAsync(admin, Dia15, FimDoMes); // o dia 15 está nas duas
        sobreposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Codigo(sobreposta)).Should().Be("sobreposicao");
        (await CriarAsync(admin, Dia16, FimDoMes)).StatusCode.Should().Be(HttpStatusCode.Created); // emendada é aceita

        // 10 criações simultâneas do mesmo período (mês seguinte): o banco deixa passar uma só.
        var proximo = Dia1.AddMonths(1);
        var respostas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => CriarAsync(admin, proximo, proximo.AddDays(14))));
        respostas.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);
        (await _fabrica.NoBancoAsync(db => db.PeriodosComissao.IgnoreQueryFilters().CountAsync(p => p.NegocioId == negocioId))).Should().Be(3);

        (await CriarAsync(admin, Dia15, Dia1)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Atendimento_entra_na_quinzena_pela_data_do_atendimento_no_fuso_do_negocio()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await ProfissionalComAcertoAsync(admin, negocioId, "Ana", 10m);
        var primeira = await CriarIdAsync(admin, Dia1, Dia15);
        var segunda = await CriarIdAsync(admin, Dia16, FimDoMes);

        await _fabrica.SemearEConcluirAsync(admin, negocioId, ana, Local(Dia15, 23, 50)); // 02:50 UTC do dia 16
        await _fabrica.SemearEConcluirAsync(admin, negocioId, ana, Local(Dia16, 0, 10));
        await _fabrica.SemearEConcluirAsync(admin, negocioId, ana, Local(Dia16, 9, 0));

        (await DetalheAsync(admin, primeira)).Linhas.Single().Totais.QuantidadeServicos.Should().Be(1);
        var detalheSegunda = await DetalheAsync(admin, segunda);
        detalheSegunda.Parcial.Should().BeTrue();
        detalheSegunda.Linhas.Single().Totais.Should().Be(new TotaisComissao(10m, 100m, 2));
    }

    [Fact]
    public async Task Fechar_grava_os_totais_e_eles_nao_mudam_depois()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await ProfissionalComAcertoAsync(admin, negocioId, "Ana", 20m);
        var quinzena = await CriarIdAsync(admin, Dia1, Dia15);
        await _fabrica.SemearEConcluirAsync(admin, negocioId, ana, Local(Dia1.AddDays(2), 10, 0));
        await _fabrica.SemearEConcluirAsync(admin, negocioId, ana, Local(Dia1.AddDays(3), 10, 0));

        (await FecharAsync(admin, quinzena)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var fechamento = await _fabrica.NoBancoAsync(db => db.FechamentosComissao.IgnoreQueryFilters().SingleAsync(f => f.PeriodoComissaoId == quinzena));
        (fechamento.ProfissionalId, fechamento.TotalCobrado, fechamento.TotalComissao, fechamento.QuantidadeServicos)
            .Should().Be((ana, 100m, 20m, 2));

        // Mudar o percentual e mexer nos dados de linha depois do fechamento não muda o valor fechado.
        await admin.DefinirComissaoAsync(ana, 90m, acertoPorQuinzena: true);
        await _fabrica.NoBancoAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE agendamento_servicos SET comissao_valor = 999 WHERE comissao_profissional_id = {0}", ana));

        var detalhe = await DetalheAsync(admin, quinzena);
        detalhe.Parcial.Should().BeFalse();
        detalhe.Quinzena.Estado.Should().Be("Fechada");
        detalhe.Linhas.Single().Totais.Should().Be(new TotaisComissao(20m, 100m, 2));
        (await FecharAsync(admin, quinzena)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concluir_reabrir_ou_cancelar_atendimento_de_quinzena_fechada_e_recusado()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await ProfissionalComAcertoAsync(admin, negocioId, "Ana", 10m);
        var quinzena = await CriarIdAsync(admin, Dia1, Dia15);
        var concluido = await _fabrica.SemearEConcluirAsync(admin, negocioId, ana, Local(Dia1.AddDays(1), 10, 0));
        var pendente = await _fabrica.SemearAgendamentoAsync(negocioId, ana, Local(Dia1.AddDays(2), 10, 0));
        var outroPendente = await _fabrica.SemearAgendamentoAsync(negocioId, ana, Local(Dia1.AddDays(3), 10, 0));
        (await FecharAsync(admin, quinzena, confirmar: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        foreach (var (id, acao) in new[] { (pendente, "concluir"), (concluido, "reabrir"), (outroPendente, "cancelar") })
        {
            var resposta = await admin.PostAsync($"/painel/agendamentos/{id}/{acao}", null);
            resposta.StatusCode.Should().Be(HttpStatusCode.Conflict, acao);
            (await Codigo(resposta)).Should().Be("quinzena_fechada", acao);
        }

        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == concluido))).Status
            .Should().Be(Plataforma.Dominio.Agendamentos.StatusAgendamento.Concluido);
        (await _fabrica.NoBancoAsync(db => db.Pagamentos.IgnoreQueryFilters().AnyAsync(p => p.AgendamentoId == concluido))).Should().BeFalse();
    }

    [Fact]
    public async Task Reabrir_exige_motivo_apaga_os_fechamentos_e_fica_na_auditoria()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await ProfissionalComAcertoAsync(admin, negocioId, "Ana", 10m);
        var quinzena = await CriarIdAsync(admin, Dia1, Dia15);
        var pendente = await _fabrica.SemearAgendamentoAsync(negocioId, ana, Local(Dia1.AddDays(2), 10, 0));
        await FecharAsync(admin, quinzena, confirmar: true);

        (await ReabrirAsync(admin, quinzena, "  ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReabrirAsync(admin, quinzena, "Faltou lançar o atendimento do dia 3")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _fabrica.NoBancoAsync(db => db.FechamentosComissao.IgnoreQueryFilters().AnyAsync(f => f.PeriodoComissaoId == quinzena))).Should().BeFalse();
        (await DetalheAsync(admin, quinzena)).Quinzena.Estado.Should().Be("Aberta");
        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
                .Where(l => l.EntidadeId == quinzena).OrderBy(l => l.CriadoEm).Select(l => l.Acao).ToListAsync()))
            .Should().Equal("CriarQuinzena", "FecharQuinzena", "ReabrirQuinzena");
        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
                .SingleAsync(l => l.EntidadeId == quinzena && l.Acao == "ReabrirQuinzena"))).Detalhes
            .Should().Contain("Motivo: Faltou lançar o atendimento do dia 3");

        // Reaberta, o atendimento volta a poder ser concluído; fechar de novo gera outro fechamento.
        (await admin.PostAsync($"/painel/agendamentos/{pendente}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FecharAsync(admin, quinzena)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DetalheAsync(admin, quinzena)).Linhas.Single().Totais.Should().Be(new TotaisComissao(5m, 50m, 1));
        (await ReabrirAsync(admin, Guid.NewGuid(), "x")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Fechar_com_atendimentos_sem_conclusao_exige_confirmacao()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await ProfissionalComAcertoAsync(admin, negocioId, "Ana", 10m);
        var quinzena = await CriarIdAsync(admin, Dia1, Dia15);
        var pendente = await _fabrica.SemearAgendamentoAsync(negocioId, ana, Local(Dia1.AddDays(4), 15, 0));

        var semConfirmar = await FecharAsync(admin, quinzena);
        semConfirmar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = await semConfirmar.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("codigo").GetString().Should().Be("pendentes");
        corpo.GetProperty("pendentes").EnumerateArray().Select(p => p.GetProperty("agendamentoId").GetGuid()).Should().Equal(pendente);
        (await DetalheAsync(admin, quinzena)).Quinzena.Estado.Should().Be("Aberta");

        (await FecharAsync(admin, quinzena, confirmar: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
                .SingleAsync(l => l.EntidadeId == quinzena && l.Acao == "FecharQuinzena"))).Detalhes
            .Should().Contain("1 atendimento(s) sem conclusão");
    }

    [Fact]
    public async Task Profissional_so_ve_os_proprios_valores()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await ProfissionalComAcertoAsync(admin, negocioId, "Ana", 10m);
        var beto = await ProfissionalComAcertoAsync(admin, negocioId, "Beto", 50m);
        var fechada = await CriarIdAsync(admin, Dia1, Dia15);
        var aberta = await CriarIdAsync(admin, Dia16, FimDoMes);
        await _fabrica.SemearEConcluirAsync(admin, negocioId, ana, Local(Dia1.AddDays(1), 10, 0));
        await _fabrica.SemearEConcluirAsync(admin, negocioId, beto, Local(Dia1.AddDays(1), 11, 0));
        await _fabrica.SemearEConcluirAsync(admin, negocioId, ana, Local(Dia16.AddDays(1), 10, 0));
        await _fabrica.SemearEConcluirAsync(admin, negocioId, beto, Local(Dia16.AddDays(1), 11, 0));
        await FecharAsync(admin, fechada);

        using var clienteAna = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, ana);
        var minhas = await clienteAna.GetFromJsonAsync<QuinzenasDoProfissional>("/painel/comissoes/minhas/quinzenas");

        minhas!.AcertoPorQuinzena.Should().BeTrue();
        minhas.Quinzenas.Should().HaveCount(2);
        minhas.Quinzenas.Single(q => q.PeriodoId == aberta).Should().Match<QuinzenaDoProfissional>(q => q.Parcial && q.Totais.TotalComissao == 5m);
        minhas.Quinzenas.Single(q => q.PeriodoId == fechada).Should().Match<QuinzenaDoProfissional>(q => !q.Parcial && q.Totais.TotalComissao == 5m);

        (await clienteAna.GetAsync("/painel/quinzenas")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await clienteAna.GetAsync($"/painel/quinzenas/{fechada}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await FecharAsync(clienteAna, aberta)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Profissional_sem_acerto_por_quinzena_nao_entra_no_fechamento_nem_fica_travado()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await ProfissionalComAcertoAsync(admin, negocioId, "Ana", 10m);
        var caio = await _fabrica.CriarProfissionalAsync(negocioId, "Caio");
        await admin.DefinirComissaoAsync(caio, 30m, acertoPorQuinzena: false);
        var quinzena = await CriarIdAsync(admin, Dia1, Dia15);
        await _fabrica.SemearEConcluirAsync(admin, negocioId, caio, Local(Dia1.AddDays(1), 10, 0));
        var pendenteDoCaio = await _fabrica.SemearAgendamentoAsync(negocioId, caio, Local(Dia1.AddDays(2), 10, 0));

        (await FecharAsync(admin, quinzena)).StatusCode.Should().Be(HttpStatusCode.NoContent); // o pendente do Caio não conta

        (await DetalheAsync(admin, quinzena)).Linhas.Select(l => l.ProfissionalId).Should().Equal(ana);
        (await admin.PostAsync($"/painel/agendamentos/{pendenteDoCaio}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var clienteCaio = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, caio);
        var minhas = await clienteCaio.GetFromJsonAsync<QuinzenasDoProfissional>("/painel/comissoes/minhas/quinzenas");
        minhas!.AcertoPorQuinzena.Should().BeFalse();
        minhas.Quinzenas.Should().BeEmpty();
    }

    [Fact]
    public async Task Sugestao_segue_a_ultima_e_buraco_gera_aviso()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await CriarIdAsync(admin, Dia1, Dia15);

        var sugestao = await admin.GetFromJsonAsync<SugestaoQuinzena>("/painel/quinzenas/sugestao");
        sugestao.Should().Be(new SugestaoQuinzena(Dia16, FimDoMes));

        var comBuraco = await CriarAsync(admin, Dia1.AddMonths(1), Dia1.AddMonths(1).AddDays(14));
        comBuraco.StatusCode.Should().Be(HttpStatusCode.Created);
        (await comBuraco.Content.ReadFromJsonAsync<QuinzenaSalvaResposta>())!.Aviso.Should().Contain("dias sem quinzena");

        var lista = await admin.GetFromJsonAsync<List<QuinzenaResumo>>("/painel/quinzenas");
        lista!.First().DiasSemPeriodoAntes.Should().Be(FimDoMes.DayNumber - Dia15.DayNumber);
    }

    [Fact]
    public async Task Quinzena_fechada_nao_pode_ser_editada_nem_excluida_e_aberta_pode()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var quinzena = await CriarIdAsync(admin, Dia1, Dia15);

        (await admin.PutAsJsonAsync($"/painel/quinzenas/{quinzena}", new DatasQuinzenaRequisicao(Dia1, Dia1.AddDays(13))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await FecharAsync(admin, quinzena);
        (await admin.PutAsJsonAsync($"/painel/quinzenas/{quinzena}", new DatasQuinzenaRequisicao(Dia1, Dia15)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/painel/quinzenas/{quinzena}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await ReabrirAsync(admin, quinzena, "corrigir datas");
        (await admin.DeleteAsync($"/painel/quinzenas/{quinzena}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---------------------------------------------------------------- apoio

    private async Task<Guid> ProfissionalComAcertoAsync(HttpClient admin, Guid negocioId, string nome, decimal percentual)
    {
        var id = await _fabrica.CriarProfissionalAsync(negocioId, nome);
        (await admin.DefinirComissaoAsync(id, percentual, acertoPorQuinzena: true)).EnsureSuccessStatusCode();
        return id;
    }

    private static Task<HttpResponseMessage> CriarAsync(HttpClient cliente, DateOnly inicio, DateOnly fim) =>
        cliente.PostAsJsonAsync("/painel/quinzenas", new DatasQuinzenaRequisicao(inicio, fim));

    private static async Task<Guid> CriarIdAsync(HttpClient cliente, DateOnly inicio, DateOnly fim)
    {
        var resposta = await CriarAsync(cliente, inicio, fim);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resposta.Content.ReadFromJsonAsync<QuinzenaSalvaResposta>())!.Id;
    }

    private static Task<HttpResponseMessage> FecharAsync(HttpClient cliente, Guid quinzena, bool confirmar = false) =>
        cliente.PostAsJsonAsync($"/painel/quinzenas/{quinzena}/fechar", new FecharQuinzenaRequisicao(confirmar));

    private static Task<HttpResponseMessage> ReabrirAsync(HttpClient cliente, Guid quinzena, string motivo) =>
        cliente.PostAsJsonAsync($"/painel/quinzenas/{quinzena}/reabrir", new ReabrirQuinzenaRequisicao(motivo));

    private static async Task<DetalheQuinzena> DetalheAsync(HttpClient cliente, Guid quinzena) =>
        (await cliente.GetFromJsonAsync<DetalheQuinzena>($"/painel/quinzenas/{quinzena}"))!;

    private static async Task<string?> Codigo(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString();
}
