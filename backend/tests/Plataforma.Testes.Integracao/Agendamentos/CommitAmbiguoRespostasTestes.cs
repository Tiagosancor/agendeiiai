using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Cupons;
using Plataforma.Dominio.Fidelidade;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// B7.2 — COMMIT ambíguo nas operações cujo efeito é aplicado uma vez mas cuja repetição devolvia 400/409/500 enganoso,
/// perdia aviso ou duplicava histórico. Na repetição da estratégia o fluxo reconhece o que a própria requisição já gravou e
/// segue até a notificação, que sai uma vez. Aqui o COMMIT confirmado é simulado por <see cref="InterceptadorDeCommit"/>;
/// as operações sem transação explícita e o caso de conexão derrubada de verdade estão em <see cref="CommitAmbiguoProxyTestes"/>.
/// Provedores Fake: nenhuma comunicação real.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class CommitAmbiguoRespostasTestes : IAsyncLifetime
{
    private const string EmailProf = "prof.b72@teste.com";
    private const string EmailDestino = "destino.b72@teste.com";
    private const string EmailPublico = "publico.b72@teste.com";

    private readonly PostgresContainerFixture _postgres;
    private readonly InterceptadorDeCommit _commit = new();
    private PlataformaWebApplicationFactory _fabrica = null!;

    public CommitAmbiguoRespostasTestes(PostgresContainerFixture postgres) => _postgres = postgres;

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

    // ------------------------------------------------------------------ concluir

    [Fact]
    public async Task Concluir_com_commit_ambiguo_responde_sucesso_com_comissao_e_selo_uma_vez()
    {
        var c = await ArranjarAsync(comissao: 40m, fidelidade: true);
        var id = await SemearAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsync($"/painel/agendamentos/{id}/concluir", null);

        resposta.IsSuccessStatusCode.Should().BeTrue(await resposta.Content.ReadAsStringAsync());
        _commit.Falhas.Should().Be(1);
        var estado = await EstadoAsync(id);
        estado.Status.Should().Be(StatusAgendamento.Concluido);
        estado.Servicos.Single().ComissaoValor.Should().Be(20m, "40% de R$ 50, aplicado uma vez");
        (await SelosAsync(id)).Should().Be(1);
    }

    [Fact]
    public async Task Concluir_de_novo_numa_requisicao_nova_continua_recusado_com_409()
    {
        var c = await ArranjarAsync(comissao: 40m);
        var id = await SemearAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        (await c.Admin.PostAsync($"/painel/agendamentos/{id}/concluir", null)).IsSuccessStatusCode.Should().BeTrue();
        _commit.Desarmar();
        var segunda = await c.Admin.PostAsync($"/painel/agendamentos/{id}/concluir", null);

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict, "só a repetição interna da mesma requisição vira sucesso");
        (await EstadoAsync(id)).Servicos.Single().ComissaoValor.Should().Be(20m);
    }

    [Fact]
    public async Task Concluir_sem_falha_continua_igual()
    {
        var c = await ArranjarAsync(comissao: 40m, fidelidade: true);
        var id = await SemearAsync(c, 10);

        _commit.Desarmar();
        var resposta = await c.Admin.PostAsync($"/painel/agendamentos/{id}/concluir", null);

        resposta.IsSuccessStatusCode.Should().BeTrue();
        _commit.Falhas.Should().Be(0);
        _commit.Commits.Should().Be(1);
        (await EstadoAsync(id)).Servicos.Single().ComissaoValor.Should().Be(20m);
        (await SelosAsync(id)).Should().Be(1);
    }

    // ------------------------------------------------------------------ cancelar

    [Fact]
    public async Task Cancelar_com_commit_ambiguo_responde_204_e_avisa_o_profissional_uma_vez()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);
        LimparEmails();

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsync($"/painel/agendamentos/{id}/cancelar", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent, "antes da B7.2 a repetição relia o cancelamento e dava 500");
        _commit.Falhas.Should().Be(1);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Cancelado);
        Avisos(EmailProf).Should().Be(1, "o aviso saía zero vezes; agora sai uma, depois do commit reconhecido");
    }

    [Fact]
    public async Task Cancelar_com_falha_antes_do_commit_e_repetido_e_avisa_uma_vez()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);
        LimparEmails();

        _commit.ArmarFalhaAntesDoCommit();
        var resposta = await c.Admin.PostAsync($"/painel/agendamentos/{id}/cancelar", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _commit.Falhas.Should().Be(1);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Cancelado);
        Avisos(EmailProf).Should().Be(1);
    }

    [Fact]
    public async Task Cancelar_com_falha_definitiva_nao_avisa_ninguem_e_nao_cancela()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);
        LimparEmails();

        _commit.ArmarFalhaAntesDoCommit(transitoria: false);
        var resposta = await c.Admin.PostAsync($"/painel/agendamentos/{id}/cancelar", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Agendado);
        Avisos(EmailProf).Should().Be(0, "sem commit não há o que avisar");
    }

    [Fact]
    public async Task Cancelar_sem_falha_avisa_uma_vez_e_cancelar_de_novo_nao_reenvia()
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);
        LimparEmails();

        _commit.Desarmar();
        (await c.Admin.PostAsync($"/painel/agendamentos/{id}/cancelar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var segunda = await c.Admin.PostAsync($"/painel/agendamentos/{id}/cancelar", null);

        segunda.IsSuccessStatusCode.Should().BeFalse("cancelar o que já está cancelado, em outra requisição, segue sendo recusado");
        Avisos(EmailProf).Should().Be(1);
    }

    // ------------------------------------------------------------------ transferir

    [Fact]
    public async Task Transferir_com_commit_ambiguo_responde_sucesso_e_avisa_o_novo_profissional_uma_vez()
    {
        var c = await ArranjarAsync();
        var destino = await CriarDestinoAsync(c);
        var id = await SemearAsync(c, 10);
        LimparEmails();

        _commit.ArmarCommitAmbiguo();
        var resposta = await Transferir(c, id, destino);

        resposta.IsSuccessStatusCode.Should().BeTrue(await resposta.Content.ReadAsStringAsync());
        _commit.Falhas.Should().Be(1);
        (await EstadoAsync(id)).ProfissionalId.Should().Be(destino);
        Avisos(EmailDestino).Should().Be(1);
        (await AuditoriaAsync(c, AcoesAuditoria.TransferirAgendamento)).Should().Be(1);
    }

    [Fact]
    public async Task Transferir_de_novo_numa_requisicao_nova_continua_recusado()
    {
        var c = await ArranjarAsync();
        var destino = await CriarDestinoAsync(c);
        var id = await SemearAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        (await Transferir(c, id, destino)).IsSuccessStatusCode.Should().BeTrue();
        _commit.Desarmar();
        var segunda = await Transferir(c, id, destino);

        segunda.IsSuccessStatusCode.Should().BeFalse("o agendamento já não está com a origem informada");
        (await AuditoriaAsync(c, AcoesAuditoria.TransferirAgendamento)).Should().Be(1);
    }

    // ------------------------------------------------------------------ confirmar reserva (painel e público)

    [Fact]
    public async Task Confirmar_reserva_pelo_painel_com_commit_ambiguo_responde_sucesso()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, c.Agenda.ClienteId);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsync($"/painel/agendamentos/{id}/confirmar", null);

        resposta.IsSuccessStatusCode.Should().BeTrue(await resposta.Content.ReadAsStringAsync());
        _commit.Falhas.Should().Be(1);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Agendado);
    }

    [Fact]
    public async Task Confirmar_reserva_pelo_painel_de_novo_numa_requisicao_nova_continua_recusado()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, c.Agenda.ClienteId);

        _commit.Desarmar();
        (await c.Admin.PostAsync($"/painel/agendamentos/{id}/confirmar", null)).IsSuccessStatusCode.Should().BeTrue();
        var segunda = await c.Admin.PostAsync($"/painel/agendamentos/{id}/confirmar", null);

        segunda.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirmacao_publica_com_commit_ambiguo_confirma_uma_vez_e_envia_as_duas_notificacoes()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, null);
        await CriarCupomAsync(c);
        LimparEmails();

        _commit.ArmarCommitAmbiguo();
        var resultado = await ConfirmarPublicoAsync(c, id);

        resultado.Sucesso.Should().BeTrue(resultado.MensagemErro);
        _commit.Falhas.Should().Be(1);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Agendado);
        (await UsosDoCupomAsync(c)).Should().Be(1, "o cupom conta um uso só");
        Avisos(EmailPublico).Should().Be(1, "confirmação ao cliente, que antes se perdia");
        Avisos(EmailProf).Should().Be(1, "aviso ao profissional, que antes se perdia");
    }

    [Fact]
    public async Task Confirmacao_publica_com_falha_antes_do_commit_e_repetida_e_notifica_uma_vez()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, null);
        LimparEmails();

        _commit.ArmarFalhaAntesDoCommit();
        var resultado = await ConfirmarPublicoAsync(c, id);

        resultado.Sucesso.Should().BeTrue(resultado.MensagemErro);
        _commit.Falhas.Should().Be(1);
        Avisos(EmailPublico).Should().Be(1);
        Avisos(EmailProf).Should().Be(1);
    }

    [Fact]
    public async Task Confirmacao_publica_com_falha_definitiva_nao_confirma_nem_notifica()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, null);
        LimparEmails();

        _commit.ArmarFalhaAntesDoCommit(transitoria: false);
        var acao = () => ConfirmarPublicoAsync(c, id);

        await acao.Should().ThrowAsync<InvalidOperationException>();
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Reservado);
        Avisos(EmailPublico).Should().Be(0);
        Avisos(EmailProf).Should().Be(0);
    }

    [Fact]
    public async Task Confirmacao_publica_repetida_pelo_mesmo_telefone_em_outra_requisicao_nao_reenvia_avisos()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, null);
        LimparEmails();

        _commit.Desarmar();
        (await ConfirmarPublicoAsync(c, id)).Sucesso.Should().BeTrue();
        var segunda = await ConfirmarPublicoAsync(c, id);

        segunda.Sucesso.Should().BeTrue("duplo clique devolve o mesmo resultado");
        Avisos(EmailPublico).Should().Be(1);
        Avisos(EmailProf).Should().Be(1);
    }

    // ------------------------------------------------------------------ ajuste de valor

    [Fact]
    public async Task Ajuste_de_valor_com_commit_ambiguo_mantem_o_valor_e_grava_uma_linha_de_historico_e_uma_auditoria()
    {
        var c = await ArranjarAsync();
        var (id, linha) = await SemearEmAtendimentoAsync(c);

        _commit.ArmarCommitAmbiguo();
        var resposta = await Ajustar(c, id, linha, 10m);

        resposta.IsSuccessStatusCode.Should().BeTrue(await resposta.Content.ReadAsStringAsync());
        _commit.Falhas.Should().Be(1);
        (await EstadoAsync(id)).Servicos.Single().ValorCobrado.Should().Be(40m);
        (await HistoricoAsync(id)).Should().Equal("50->40");
        (await AuditoriaAsync(c, AcoesAuditoria.AjustarValorAtendimento)).Should().Be(1);
    }

    [Fact]
    public async Task Ajuste_de_valor_legitimo_seguinte_e_uma_nova_entrada_de_historico()
    {
        var c = await ArranjarAsync();
        var (id, linha) = await SemearEmAtendimentoAsync(c);

        _commit.ArmarCommitAmbiguo();
        (await Ajustar(c, id, linha, 10m)).IsSuccessStatusCode.Should().BeTrue();
        _commit.Desarmar();
        (await Ajustar(c, id, linha, 20m)).IsSuccessStatusCode.Should().BeTrue();

        (await EstadoAsync(id)).Servicos.Single().ValorCobrado.Should().Be(30m);
        (await HistoricoAsync(id)).Should().Equal("50->40", "40->30");
        (await AuditoriaAsync(c, AcoesAuditoria.AjustarValorAtendimento)).Should().Be(2);
    }

    [Fact]
    public async Task Ajuste_de_valor_com_falha_antes_do_commit_e_repetido_e_grava_uma_vez()
    {
        var c = await ArranjarAsync();
        var (id, linha) = await SemearEmAtendimentoAsync(c);

        _commit.ArmarFalhaAntesDoCommit();
        var resposta = await Ajustar(c, id, linha, 10m);

        resposta.IsSuccessStatusCode.Should().BeTrue();
        _commit.Falhas.Should().Be(1);
        (await HistoricoAsync(id)).Should().Equal("50->40");
    }

    [Fact]
    public async Task Ajuste_de_valor_com_falha_definitiva_nao_vira_sucesso()
    {
        var c = await ArranjarAsync();
        var (id, linha) = await SemearEmAtendimentoAsync(c);

        _commit.ArmarFalhaAntesDoCommit(transitoria: false);
        var resposta = await Ajustar(c, id, linha, 10m);

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await EstadoAsync(id)).Servicos.Single().ValorCobrado.Should().Be(50m);
        (await HistoricoAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Ajuste_de_valor_sem_falha_continua_igual()
    {
        var c = await ArranjarAsync();
        var (id, linha) = await SemearEmAtendimentoAsync(c);

        _commit.Desarmar();
        (await Ajustar(c, id, linha, 10m)).IsSuccessStatusCode.Should().BeTrue();

        _commit.Falhas.Should().Be(0);
        (await HistoricoAsync(id)).Should().Equal("50->40");
    }

    // ------------------------------------------------------------------ profissional

    [Fact]
    public async Task Criar_profissional_com_commit_ambiguo_responde_201_em_vez_de_conflito()
    {
        var c = await ArranjarAsync();

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/profissionais", new { nome = "Profissional Novo B72" });

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        _commit.Falhas.Should().Be(1);
        var id = await resposta.Content.ReadFromJsonAsync<Guid>();
        var lista = await _fabrica.NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters()
            .Where(p => p.NegocioId == c.NegocioId && p.Nome == "Profissional Novo B72").Select(p => p.Id).ToListAsync());
        lista.Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public async Task Criar_profissional_com_acesso_e_commit_ambiguo_cria_um_profissional_e_um_usuario()
    {
        var c = await ArranjarAsync();
        var email = $"acesso-{Guid.NewGuid():N}@teste.com";

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/profissionais", new
        {
            nome = "Profissional Com Acesso B72",
            acesso = new { email, senha = "Senha!Forte123", enviarConvite = false },
        });

        resposta.StatusCode.Should().Be(HttpStatusCode.Created, await resposta.Content.ReadAsStringAsync());
        var usuarios = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().CountAsync(u => u.Email == email));
        usuarios.Should().Be(1);
    }

    [Fact]
    public async Task Criar_profissional_de_novo_numa_requisicao_nova_cria_outro()
    {
        var c = await ArranjarAsync();

        _commit.ArmarCommitAmbiguo();
        var primeira = await c.Admin.PostAsJsonAsync("/painel/profissionais", new { nome = "Profissional Repetido B72" });
        _commit.Desarmar();
        var segunda = await c.Admin.PostAsJsonAsync("/painel/profissionais", new { nome = "Profissional Repetido B72" });

        (await primeira.Content.ReadFromJsonAsync<Guid>()).Should().NotBe(await segunda.Content.ReadFromJsonAsync<Guid>());
        (await _fabrica.NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().CountAsync(p => p.NegocioId == c.NegocioId && p.Nome == "Profissional Repetido B72")))
            .Should().Be(2);
    }

    // ------------------------------------------------------------------ tenant

    [Fact]
    public async Task Commit_ambiguo_de_um_negocio_nao_reconhece_nem_altera_dados_de_outro()
    {
        var a = await ArranjarAsync();
        var b = await ArranjarAsync();
        var (idA, linhaA) = await SemearEmAtendimentoAsync(a);
        var (idB, linhaB) = await SemearEmAtendimentoAsync(b);
        var cancelarB = await SemearAsync(b, 15);

        _commit.Desarmar();
        (await Ajustar(b, idB, linhaB, 5m)).IsSuccessStatusCode.Should().BeTrue();
        _commit.ArmarCommitAmbiguo();
        (await Ajustar(a, idA, linhaA, 10m)).IsSuccessStatusCode.Should().BeTrue();
        _commit.ArmarCommitAmbiguo();
        (await b.Admin.PostAsync($"/painel/agendamentos/{cancelarB}/cancelar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await HistoricoAsync(idA)).Should().Equal("50->40");
        (await HistoricoAsync(idB)).Should().Equal("50->45");
        (await EstadoAsync(idB)).Servicos.Single().ValorCobrado.Should().Be(45m);
        (await EstadoAsync(cancelarB)).Status.Should().Be(StatusAgendamento.Cancelado);
        (await AuditoriaAsync(a, AcoesAuditoria.AjustarValorAtendimento)).Should().Be(1);
        (await AuditoriaAsync(b, AcoesAuditoria.AjustarValorAtendimento)).Should().Be(1);
    }

    // ------------------------------------------------------------------ apoio

    private sealed record Cenario(Guid NegocioId, HttpClient Admin, SemeadorDeAgenda.CenarioDeAgenda Agenda, DateOnly Segunda);

    private async Task<Cenario> ArranjarAsync(decimal comissao = 0m, bool fidelidade = false)
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador,
            Permissao.AjustarValorAtendimento, Permissao.GerenciarComissoes, Permissao.GerenciarProfissionais, Permissao.GerenciarUsuarios);
        var agenda = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        await _fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE profissionais SET email = {EmailProf}, percentual_comissao = {comissao} WHERE id = {agenda.ProfissionalId}");
            if (fidelidade)
                db.ProgramasFidelidade.Add(ProgramaFidelidade.Criar(negocioId, 5, "Um corte"));
            return await db.SaveChangesAsync();
        });

        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);
        return new Cenario(negocioId, admin, agenda, segunda);
    }

    private static DateTimeOffset Local(Cenario c, int hora) => SemeadorDeComissoes.Local(c.Segunda, hora, 0);

    private Task<Guid> SemearAsync(Cenario c, int hora) =>
        _fabrica.SemearAgendamentoAsync(c.NegocioId, c.Agenda.ProfissionalId, Local(c, hora), precos: [50m], servicoId: c.Agenda.ServicoId);

    private async Task<(Guid Id, Guid Linha)> SemearEmAtendimentoAsync(Cenario c)
    {
        var id = await SemearAsync(c, 10);
        (await c.Admin.PostAsync($"/painel/agendamentos/{id}/iniciar", null)).EnsureSuccessStatusCode();
        return (id, (await EstadoAsync(id)).Servicos.Single().Id);
    }

    private Task<Guid> SemearReservaAsync(Cenario c, Guid? clienteId) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var reserva = Agendamento.CriarReserva(c.NegocioId, c.Agenda.ProfissionalId, clienteId, Local(c, 10),
                [new ItemServicoAgendamento(c.Agenda.ServicoId, "Serviço de Teste", 50m, 30)], DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));
            db.Agendamentos.Add(reserva);
            await db.SaveChangesAsync();
            return reserva.Id;
        });

    private async Task<Guid> CriarDestinoAsync(Cenario c)
    {
        var destino = await _fabrica.CriarProfissionalAsync(c.NegocioId, "Destino B72");
        await _fabrica.NoBancoAsync(async db =>
        {
            foreach (var dia in Enum.GetValues<DiaSemana>())
                db.HorariosTrabalho.Add(Plataforma.Dominio.Profissionais.HorarioTrabalho.Criar(c.NegocioId, destino, dia, new TimeOnly(8, 0), new TimeOnly(18, 0)));
            db.ProfissionalServicos.Add(Plataforma.Dominio.Servicos.ProfissionalServico.Criar(c.NegocioId, destino, c.Agenda.ServicoId));
            await db.SaveChangesAsync();
            return await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET email = {EmailDestino} WHERE id = {destino}");
        });
        return destino;
    }

    private static Task<HttpResponseMessage> Transferir(Cenario c, Guid agendamento, Guid destino) =>
        c.Admin.PostAsJsonAsync($"/painel/profissionais/{c.Agenda.ProfissionalId}/agendamentos-futuros/{agendamento}/transferir", new { novoProfissionalId = destino });

    private static Task<HttpResponseMessage> Ajustar(Cenario c, Guid id, Guid linha, decimal valor) =>
        c.Admin.PostAsJsonAsync($"/painel/agendamentos/{id}/servicos/{linha}/ajuste", new AjustarValor("Desconto", "Reais", valor, "Cortesia"));

    private Task CriarCupomAsync(Cenario c) =>
        _fabrica.NoBancoAsync(async db =>
        {
            db.Cupons.Add(Cupom.Criar(c.NegocioId, "PUB", TipoDescontoCupom.Percentual, 10m, limiteUsos: 1));
            return await db.SaveChangesAsync();
        });

    private Task<int> UsosDoCupomAsync(Cenario c) =>
        _fabrica.NoBancoAsync(db => db.Cupons.IgnoreQueryFilters().Where(x => x.NegocioId == c.NegocioId).Select(x => x.UsosAtuais).SingleAsync());

    private async Task<ResultadoAgendamento> ConfirmarPublicoAsync(Cenario c, Guid reservaId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(c.NegocioId);
        return await escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>().ConfirmarReservaPublicaAsync(
            new ConfirmarReservaPublica(reservaId, "Cliente B72", "71955551234", EmailPublico, "obs", "PUB", "127.0.0.1"));
    }

    private Task<Agendamento> EstadoAsync(Guid id) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking().Include(a => a.Servicos).SingleAsync(a => a.Id == id));

    private Task<int> SelosAsync(Guid agendamentoId) =>
        _fabrica.NoBancoAsync(db => db.SelosCliente.IgnoreQueryFilters().CountAsync(s => s.AgendamentoId == agendamentoId));

    private Task<int> AuditoriaAsync(Cenario c, string acao) =>
        _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.NegocioId == c.NegocioId && l.Acao == acao));

    private Task<List<string>> HistoricoAsync(Guid agendamentoId) =>
        _fabrica.NoBancoAsync(async db => (await db.AjustesValorAtendimento.IgnoreQueryFilters().Where(a => a.AgendamentoId == agendamentoId)
            .OrderBy(a => a.CriadoEm).Select(a => new { a.ValorAntes, a.ValorDepois }).ToListAsync())
            .Select(a => $"{a.ValorAntes:0.##}->{a.ValorDepois:0.##}").ToList());

    private void LimparEmails() => _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Clear();

    private int Avisos(string email) => _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Count(e => e.Destinatario == email);
}
