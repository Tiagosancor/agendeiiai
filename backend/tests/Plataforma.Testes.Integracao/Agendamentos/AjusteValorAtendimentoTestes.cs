using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.Infraestrutura.SemeadorDeComissoes;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>Iniciar atendimento e ajuste de valor durante o atendimento (seção 7, item 8 da seção 14).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AjusteValorAtendimentoTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public AjusteValorAtendimentoTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Sem_a_permissao_Recepcionista_e_Profissional_recebem_403_e_nao_veem_o_botao()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await _fabrica.CriarProfissionalAsync(negocioId, "Ana");
        var (agendamentoId, linhaId) = await EmAtendimentoAsync(admin, negocioId, ana);

        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista);
        using var profissional = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, ana);

        foreach (var cliente in new[] { recepcao, profissional })
        {
            (await AjustarAsync(cliente, agendamentoId, linhaId, "Desconto", "Reais", 5m, "Cliente fiel"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await cliente.GetFromJsonAsync<ValoresAtendimento>($"/painel/agendamentos/{agendamentoId}/valores"))!
                .PodeAjustar.Should().BeFalse();
        }

        (await LinhaAsync(linhaId)).PrecoAjustado.Should().BeNull();
    }

    [Fact]
    public async Task Profissional_com_a_permissao_ajusta_so_os_atendimentos_dele()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await _fabrica.CriarProfissionalAsync(negocioId, "Ana");
        var beto = await _fabrica.CriarProfissionalAsync(negocioId, "Beto");
        var (daAna, linhaDaAna) = await EmAtendimentoAsync(admin, negocioId, ana);
        var (doBeto, linhaDoBeto) = await EmAtendimentoAsync(admin, negocioId, beto, hora: 11);

        using var clienteAna = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, ana, Permissao.AjustarValorAtendimento);

        (await AjustarAsync(clienteAna, daAna, linhaDaAna, "Desconto", "Percentual", 10m, "Cliente fiel")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AjustarAsync(clienteAna, doBeto, linhaDoBeto, "Desconto", "Percentual", 10m, "Cliente fiel")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await LinhaAsync(linhaDaAna)).PrecoAjustado.Should().Be(45m);
        (await LinhaAsync(linhaDoBeto)).PrecoAjustado.Should().BeNull();
        (await clienteAna.GetAsync($"/painel/agendamentos/{doBeto}/valores")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A Recepcionista com a permissão ajusta os de qualquer profissional.
        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista, null, Permissao.AjustarValorAtendimento);
        (await AjustarAsync(recepcao, doBeto, linhaDoBeto, "Acrescimo", "Reais", 15m, "Hidratação extra")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LinhaAsync(linhaDoBeto)).PrecoAjustado.Should().Be(65m);
    }

    [Fact]
    public async Task Ajuste_so_e_aceito_com_o_atendimento_em_andamento_e_com_motivo_e_nunca_negativo()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await _fabrica.CriarProfissionalAsync(negocioId, "Ana");
        var agendado = await _fabrica.SemearAgendamentoAsync(negocioId, ana, Local(Dia(-1), 9, 0));
        var linhaAgendado = (await LinhasAsync(agendado)).Single().Id;

        (await AjustarAsync(admin, agendado, linhaAgendado, "Desconto", "Reais", 5m, "Cliente fiel")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var (emAtendimento, linha) = await EmAtendimentoAsync(admin, negocioId, ana, hora: 10);
        (await AjustarAsync(admin, emAtendimento, linha, "Desconto", "Reais", 5m, "   ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var negativo = await AjustarAsync(admin, emAtendimento, linha, "Desconto", "Reais", 60m, "Cortesia");
        negativo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await negativo.Content.ReadAsStringAsync()).Should().Contain("negativo");
        (await AjustarAsync(admin, emAtendimento, linha, "Desconto", "Percentual", 120m, "Cortesia")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await AjustarAsync(admin, emAtendimento, linha, "Desconto", "Reais", 50m, "Cortesia")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LinhaAsync(linha)).PrecoAjustado.Should().Be(0m);
    }

    [Fact]
    public async Task Preco_original_e_cadastro_do_servico_nao_mudam_e_o_ajuste_vale_para_pagamento_faturamento_e_comissao()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        await admin.DefinirComissaoAsync(cenario.ProfissionalId, 10m);
        var dia = Dia(-1);
        var agendamentoId = await _fabrica.SemearAgendamentoAsync(negocioId, cenario.ProfissionalId, Local(dia, 10, 0), servicoId: cenario.ServicoId);
        var linha = (await LinhasAsync(agendamentoId)).Single().Id;
        await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/iniciar", null);

        // Desconto de 20% sobre R$ 50,00, depois trocado por um acréscimo de R$ 10,00: o ajuste substitui, não acumula.
        (await AjustarAsync(admin, agendamentoId, linha, "Desconto", "Percentual", 20m, "Primeira visita")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AjustarAsync(admin, agendamentoId, linha, "Acrescimo", "Reais", 10m, "Barba incluída")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var gravada = await LinhaAsync(linha);
        (gravada.Preco, gravada.PrecoAjustado, gravada.ComissaoValorBase, gravada.ComissaoValor).Should().Be((50m, 60m, 60m, 6m));
        (await _fabrica.NoBancoAsync(db => db.Servicos.IgnoreQueryFilters().SingleAsync(s => s.Id == cenario.ServicoId))).Preco.Should().Be(50m);

        var agenda = await admin.GetFromJsonAsync<List<AgendamentoResumo>>($"/painel/agenda?profissionalId={cenario.ProfissionalId}&data={dia:yyyy-MM-dd}");
        agenda!.Single().Total.Should().Be(60m); // é o que a tela sugere no "Registrar pagamento"

        (await admin.PostAsJsonAsync("/painel/pagamentos", new RegistrarPagamento(agendamentoId, 60m, "Pix"))).EnsureSuccessStatusCode();
        var resumo = await admin.GetFromJsonAsync<ResumoFinanceiro>($"/painel/financeiro/resumo?inicio={dia:yyyy-MM-dd}&fim={dia:yyyy-MM-dd}");
        resumo!.Total.Should().Be(60m);
        resumo.PorServico.Single().Total.Should().Be(60m);

        var valores = await admin.GetFromJsonAsync<ValoresAtendimento>($"/painel/agendamentos/{agendamentoId}/valores");
        valores!.Linhas.Single().Ajustes.Select(a => (a.ValorAntes, a.ValorDepois, a.Motivo))
            .Should().Equal((50m, 40m, "Primeira visita"), (40m, 60m, "Barba incluída"));

        var auditoria = await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .Where(l => l.EntidadeId == agendamentoId && l.Acao == "AjustarValorAtendimento").OrderBy(l => l.CriadoEm).Select(l => l.Detalhes).ToListAsync());
        auditoria.Should().HaveCount(2);
        auditoria[0].Should().Contain("R$ 50,00 → R$ 40,00").And.Contain("desconto de 20%").And.Contain("Motivo: Primeira visita");
        auditoria[1].Should().Contain("R$ 40,00 → R$ 60,00").And.Contain("acréscimo de R$ 10,00");
    }

    [Fact]
    public async Task Horario_de_atendimento_em_andamento_continua_ocupado()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegunda();
        var inicio = Local(segunda, 10, 0);

        var criar = await admin.PostAsJsonAsync("/painel/agendamentos",
            new CriarAgendamento(cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio));
        var agendamentoId = await criar.Content.ReadFromJsonAsync<Guid>();
        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/iniciar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var porCima = await admin.PostAsJsonAsync("/painel/agendamentos",
            new CriarAgendamento(cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio));
        porCima.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var livres = await admin.GetFromJsonAsync<List<DateTimeOffset>>(
            $"/painel/profissionais/{cenario.ProfissionalId}/horarios-livres?data={segunda:yyyy-MM-dd}&duracaoMinutos={cenario.DuracaoMinutos}");
        livres.Should().NotContain(h => h.ToUniversalTime() == inicio);

        // A garantia é do banco: um insert direto por cima também é recusado.
        var direto = () => _fabrica.SemearAgendamentoAsync(negocioId, cenario.ProfissionalId, inicio.AddMinutes(10));
        await direto.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Depois_de_concluido_so_o_Administrador_corrige_recalculando_a_comissao_e_nunca_em_quinzena_fechada()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await _fabrica.CriarProfissionalAsync(negocioId, "Ana");
        await admin.DefinirComissaoAsync(ana, 50m, acertoPorQuinzena: true);
        var (agendamentoId, linha) = await EmAtendimentoAsync(admin, negocioId, ana);
        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/concluir", null)).EnsureSuccessStatusCode();
        (await LinhaAsync(linha)).ComissaoValor.Should().Be(25m);

        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista, null, Permissao.AjustarValorAtendimento);
        (await AjustarAsync(recepcao, agendamentoId, linha, "Desconto", "Reais", 10m, "Erro no lançamento")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await AjustarAsync(admin, agendamentoId, linha, "Desconto", "Reais", 10m, "Erro no lançamento")).StatusCode.Should().Be(HttpStatusCode.OK);
        var corrigida = await LinhaAsync(linha);
        (corrigida.PrecoAjustado, corrigida.ComissaoValorBase, corrigida.ComissaoPercentual, corrigida.ComissaoValor).Should().Be((40m, 40m, 50m, 20m));
        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .AnyAsync(l => l.EntidadeId == agendamentoId && l.Acao == "CorrigirValorAtendimento"))).Should().BeTrue();

        // Quinzena fechada: nem o Administrador corrige.
        var dia = Dia(-1);
        var quinzena = await admin.PostAsJsonAsync("/painel/quinzenas", new DatasQuinzenaRequisicao(dia, dia));
        var quinzenaId = (await quinzena.Content.ReadFromJsonAsync<QuinzenaSalvaResposta>())!.Id;
        (await admin.PostAsJsonAsync($"/painel/quinzenas/{quinzenaId}/fechar", new FecharQuinzenaRequisicao(false))).EnsureSuccessStatusCode();

        (await AjustarAsync(admin, agendamentoId, linha, "Desconto", "Reais", 20m, "Mais um erro")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LinhaAsync(linha)).PrecoAjustado.Should().Be(40m);
    }

    [Fact]
    public async Task Iniciar_so_a_partir_de_agendado_e_a_quinzena_pede_confirmacao_de_quem_esta_em_atendimento()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await _fabrica.CriarProfissionalAsync(negocioId, "Ana");
        await admin.DefinirComissaoAsync(ana, 10m, acertoPorQuinzena: true);
        var (agendamentoId, _) = await EmAtendimentoAsync(admin, negocioId, ana);

        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/iniciar", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/faltou", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var dia = Dia(-1);
        var quinzena = await admin.PostAsJsonAsync("/painel/quinzenas", new DatasQuinzenaRequisicao(dia, dia));
        var quinzenaId = (await quinzena.Content.ReadFromJsonAsync<QuinzenaSalvaResposta>())!.Id;
        (await admin.PostAsJsonAsync($"/painel/quinzenas/{quinzenaId}/fechar", new FecharQuinzenaRequisicao(false)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict); // pendente: está em atendimento
    }

    // ---------------------------------------------------------------- apoio

    private async Task<(Guid AgendamentoId, Guid LinhaId)> EmAtendimentoAsync(HttpClient admin, Guid negocioId, Guid profissionalId, int hora = 10)
    {
        var id = await _fabrica.SemearAgendamentoAsync(negocioId, profissionalId, Local(Dia(-1), hora, 0));
        (await admin.PostAsync($"/painel/agendamentos/{id}/iniciar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (id, (await LinhasAsync(id)).Single().Id);
    }

    private static Task<HttpResponseMessage> AjustarAsync(
        HttpClient cliente, Guid agendamentoId, Guid linhaId, string tipo, string modo, decimal valor, string motivo) =>
        cliente.PostAsJsonAsync($"/painel/agendamentos/{agendamentoId}/servicos/{linhaId}/ajuste", new AjustarValor(tipo, modo, valor, motivo));

    private Task<List<AgendamentoServico>> LinhasAsync(Guid agendamentoId) =>
        _fabrica.NoBancoAsync(db => db.Set<AgendamentoServico>().IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.AgendamentoId == agendamentoId).ToListAsync());

    private Task<AgendamentoServico> LinhaAsync(Guid linhaId) =>
        _fabrica.NoBancoAsync(db => db.Set<AgendamentoServico>().IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == linhaId));

    private static DateOnly ProximaSegunda()
    {
        var dia = Dia(2);
        while (dia.DayOfWeek != DayOfWeek.Monday)
            dia = dia.AddDays(1);
        return dia;
    }
}
