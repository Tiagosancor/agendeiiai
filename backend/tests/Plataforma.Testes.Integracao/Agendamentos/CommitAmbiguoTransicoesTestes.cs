using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Fidelidade;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// B7.3 — COMMIT ambíguo em iniciar atendimento, marcar falta e reabrir: a repetição da estratégia reconhece o estado de
/// destino que a própria requisição já gravou e responde sucesso (antes: 409 com o efeito aplicado). Só a repetição interna
/// reconhece; uma requisição nova contra estado incompatível continua recusada. Interceptador determinístico, sem espera.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class CommitAmbiguoTransicoesTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private readonly InterceptadorDeCommit _commit = new();
    private PlataformaWebApplicationFactory _fabrica = null!;

    public CommitAmbiguoTransicoesTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, null, servicos =>
            servicos.ConfigureDbContext<PlataformaDbContext>(o => o.AddInterceptors(_commit)));
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync()
    {
        await _fabrica.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    // ------------------------------------------------------------------ iniciar

    [Fact]
    public async Task Iniciar_com_commit_ambiguo_responde_204_e_deixa_em_atendimento()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        var resposta = await Post(c, id, "iniciar");

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent, "antes da B7.3 a repetição via EmAtendimento e dava 409");
        _commit.Falhas.Should().Be(1);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.EmAtendimento);
    }

    [Fact]
    public async Task Iniciar_de_novo_numa_requisicao_nova_continua_recusado_com_409()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        (await Post(c, id, "iniciar")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _commit.Desarmar();
        var segunda = await Post(c, id, "iniciar");

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict, "só a repetição interna da mesma requisição vira sucesso");
    }

    [Fact]
    public async Task Iniciar_com_falha_antes_do_commit_e_repetido_normalmente()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        _commit.ArmarFalhaAntesDoCommit();
        var resposta = await Post(c, id, "iniciar");

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _commit.Falhas.Should().Be(1);
        _commit.Commits.Should().Be(1);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.EmAtendimento);
    }

    [Fact]
    public async Task Iniciar_com_falha_definitiva_nao_vira_sucesso_e_nao_inicia()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        _commit.ArmarFalhaAntesDoCommit(transitoria: false);
        var resposta = await Post(c, id, "iniciar");

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Agendado);
    }

    [Fact]
    public async Task Iniciar_sem_falha_continua_igual()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        _commit.Desarmar();
        (await Post(c, id, "iniciar")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        _commit.Falhas.Should().Be(0);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.EmAtendimento);
    }

    // ------------------------------------------------------------------ faltou

    [Fact]
    public async Task Faltou_com_commit_ambiguo_responde_204_e_marca_a_falta()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        var resposta = await Post(c, id, "faltou");

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _commit.Falhas.Should().Be(1);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Faltou);
    }

    [Fact]
    public async Task Faltou_de_novo_numa_requisicao_nova_continua_recusado_com_409()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        (await Post(c, id, "faltou")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _commit.Desarmar();

        (await Post(c, id, "faltou")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Faltou_com_falha_antes_do_commit_e_repetido_normalmente()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        _commit.ArmarFalhaAntesDoCommit();
        var resposta = await Post(c, id, "faltou");

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _commit.Falhas.Should().Be(1);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Faltou);
    }

    [Fact]
    public async Task Faltou_com_falha_definitiva_nao_vira_sucesso_e_nao_marca()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        _commit.ArmarFalhaAntesDoCommit(transitoria: false);
        var resposta = await Post(c, id, "faltou");

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Agendado);
    }

    [Fact]
    public async Task Faltou_sem_falha_continua_igual()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        _commit.Desarmar();
        (await Post(c, id, "faltou")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        _commit.Falhas.Should().Be(0);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Faltou);
    }

    [Fact]
    public async Task Faltou_depois_de_iniciado_continua_recusado_e_iniciar_depois_de_faltou_tambem()
    {
        var c = await ArranjarAsync();
        var iniciado = await SemearAsync(c, 10);
        var faltou = await SemearAsync(c, 12);

        _commit.Desarmar();
        (await Post(c, iniciado, "iniciar")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Post(c, faltou, "faltou")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Post(c, iniciado, "faltou")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Post(c, faltou, "iniciar")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ------------------------------------------------------------------ reabrir

    [Fact]
    public async Task Reabrir_com_commit_ambiguo_responde_204_e_estorna_comissao_e_selo_uma_vez()
    {
        var c = await ArranjarAsync(comissao: 40m, fidelidade: true);
        var id = await SemearConcluidoAsync(c, 10);
        (await SelosAsync(id)).Should().Be(1);

        _commit.ArmarCommitAmbiguo();
        var resposta = await Post(c, id, "reabrir");

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent, "antes da B7.3 a repetição via Agendado e dava 409");
        _commit.Falhas.Should().Be(1);
        var estado = await EstadoAsync(id);
        estado.Status.Should().Be(StatusAgendamento.Agendado);
        estado.Servicos.Single().ComissaoValor.Should().BeNull("comissão estornada");
        (await SelosAsync(id)).Should().Be(0);
        (await AuditoriaAsync(c, AcoesAuditoria.ReabrirAtendimento)).Should().Be(1);
    }

    [Fact]
    public async Task Reabrir_de_novo_numa_requisicao_nova_continua_recusado_com_409_e_nao_duplica_auditoria()
    {
        var c = await ArranjarAsync(comissao: 40m);
        var id = await SemearConcluidoAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        (await Post(c, id, "reabrir")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _commit.Desarmar();
        var segunda = await Post(c, id, "reabrir");

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AuditoriaAsync(c, AcoesAuditoria.ReabrirAtendimento)).Should().Be(1);
    }

    [Fact]
    public async Task Reabrir_com_falha_antes_do_commit_e_repetido_normalmente()
    {
        var c = await ArranjarAsync(comissao: 40m, fidelidade: true);
        var id = await SemearConcluidoAsync(c, 10);

        _commit.ArmarFalhaAntesDoCommit();
        var resposta = await Post(c, id, "reabrir");

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _commit.Falhas.Should().Be(1);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Agendado);
        (await SelosAsync(id)).Should().Be(0);
        (await AuditoriaAsync(c, AcoesAuditoria.ReabrirAtendimento)).Should().Be(1);
    }

    [Fact]
    public async Task Reabrir_com_falha_definitiva_nao_vira_sucesso_e_mantem_concluido()
    {
        var c = await ArranjarAsync(comissao: 40m, fidelidade: true);
        var id = await SemearConcluidoAsync(c, 10);

        _commit.ArmarFalhaAntesDoCommit(transitoria: false);
        var resposta = await Post(c, id, "reabrir");

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var estado = await EstadoAsync(id);
        estado.Status.Should().Be(StatusAgendamento.Concluido);
        estado.Servicos.Single().ComissaoValor.Should().Be(20m);
        (await SelosAsync(id)).Should().Be(1);
        (await AuditoriaAsync(c, AcoesAuditoria.ReabrirAtendimento)).Should().Be(0);
    }

    [Fact]
    public async Task Reabrir_sem_falha_continua_igual_e_reabrir_o_que_nao_foi_concluido_segue_recusado()
    {
        var c = await ArranjarAsync(comissao: 40m);
        var concluido = await SemearConcluidoAsync(c, 10);
        var aberto = await SemearAsync(c, 12);

        _commit.Desarmar();
        (await Post(c, concluido, "reabrir")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var naoConcluido = await Post(c, aberto, "reabrir");

        _commit.Falhas.Should().Be(0);
        (await EstadoAsync(concluido)).Status.Should().Be(StatusAgendamento.Agendado);
        naoConcluido.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ------------------------------------------------------------------ tenant

    [Fact]
    public async Task Commit_ambiguo_de_um_negocio_nao_reconhece_nem_altera_atendimento_de_outro()
    {
        var a = await ArranjarAsync();
        var b = await ArranjarAsync();
        var idA = await SemearAsync(a, 10);
        var idB = await SemearAsync(b, 10);

        _commit.ArmarCommitAmbiguo();
        (await Post(a, idA, "iniciar")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        _commit.Desarmar();

        (await EstadoAsync(idA)).Status.Should().Be(StatusAgendamento.EmAtendimento);
        (await EstadoAsync(idB)).Status.Should().Be(StatusAgendamento.Agendado, "o outro negócio não é tocado");
        (await Post(a, idB, "iniciar")).StatusCode.Should().Be(HttpStatusCode.NotFound, "atendimento de outro negócio nem é visível");
        (await Post(b, idB, "iniciar")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ------------------------------------------------------------------ apoio

    private sealed record Cenario(Guid NegocioId, HttpClient Admin, SemeadorDeAgenda.CenarioDeAgenda Agenda, DateOnly Segunda);

    private async Task<Cenario> ArranjarAsync(decimal comissao = 0m, bool fidelidade = false)
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.VerFinanceiro);
        var agenda = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        await _fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE profissionais SET percentual_comissao = {comissao} WHERE id = {agenda.ProfissionalId}");
            if (fidelidade)
                db.ProgramasFidelidade.Add(ProgramaFidelidade.Criar(negocioId, 5, "Um corte"));
            return await db.SaveChangesAsync();
        });

        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);
        return new Cenario(negocioId, admin, agenda, segunda);
    }

    private Task<Guid> SemearAsync(Cenario c, int hora) =>
        _fabrica.SemearAgendamentoAsync(c.NegocioId, c.Agenda.ProfissionalId, SemeadorDeComissoes.Local(c.Segunda, hora, 0),
            precos: [50m], servicoId: c.Agenda.ServicoId);

    private async Task<Guid> SemearConcluidoAsync(Cenario c, int hora)
    {
        _commit.Desarmar();
        var id = await SemearAsync(c, hora);
        (await Post(c, id, "concluir")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return id;
    }

    private static Task<HttpResponseMessage> Post(Cenario c, Guid id, string acao) =>
        c.Admin.PostAsync($"/painel/agendamentos/{id}/{acao}", null);

    private Task<Agendamento> EstadoAsync(Guid id) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking().Include(a => a.Servicos).SingleAsync(a => a.Id == id));

    private Task<int> SelosAsync(Guid agendamentoId) =>
        _fabrica.NoBancoAsync(db => db.SelosCliente.IgnoreQueryFilters().CountAsync(s => s.AgendamentoId == agendamentoId));

    private Task<int> AuditoriaAsync(Cenario c, string acao) =>
        _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.NegocioId == c.NegocioId && l.Acao == acao));
}
