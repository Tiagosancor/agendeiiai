using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// B7.1 — COMMIT ambíguo: o servidor confirma e a resposta se perde, a <c>ExecutionStrategy</c> repete o callback inteiro.
/// Cada operação gera o Id do que grava <b>antes</b> da estratégia e, na repetição, reconhece o que já está gravado. Aqui o
/// COMMIT confirmado é simulado por <see cref="InterceptadorDeCommit"/> (determinístico); o caso de conexão derrubada de
/// verdade está em <see cref="CommitAmbiguoProxyTestes"/>.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class CommitAmbiguoTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private readonly InterceptadorDeCommit _commit = new();
    private PlataformaWebApplicationFactory _fabrica = null!;

    public CommitAmbiguoTestes(PostgresContainerFixture postgres) => _postgres = postgres;

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
        // Os pools de fábricas descartadas ficam abertos e, somados, esgotam as conexões do Postgres compartilhado da suíte.
        NpgsqlConnection.ClearAllPools();
    }

    // ------------------------------------------------------------------ venda

    [Fact]
    public async Task Venda_com_commit_confirmado_e_resposta_perdida_grava_uma_so_vez_e_devolve_o_mesmo_sucesso()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/vendas", new LancarVenda([new ItemLancarVenda(produto, 2, 20m)], c.Agenda.ProfissionalId, null));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        _commit.Falhas.Should().Be(1, "a falha simulada precisa ter acontecido, senão o teste não prova nada");
        var lancada = (await resposta.Content.ReadFromJsonAsync<VendaLancada>())!;
        lancada.Total.Should().Be(40m);

        var vendas = await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().Where(v => v.NegocioId == c.NegocioId)
            .Select(v => new { v.Id, v.Total, v.ValorComissao }).ToListAsync());
        vendas.Should().ContainSingle().Which.Id.Should().Be(lancada.VendaId, "a repetição reaproveita o Id da venda já confirmada");
        (await EstoqueAsync(produto)).Should().Be((8, 8, 2), "baixou uma vez: 10 − 2; movimentos = estoque inicial + a venda");
        (await AuditoriaAsync(c, AcoesAuditoria.VendaProduto)).Should().Be(1);
        (await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().Where(v => v.Id == lancada.VendaId).Select(v => v.Itens.Count).SingleAsync())).Should().Be(1);
    }

    [Fact]
    public async Task Venda_com_cliente_novo_sem_telefone_fica_com_o_cliente_certo_mesmo_havendo_outro_sem_telefone()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);
        var antigo = await CriarClienteSemTelefoneAsync(c.NegocioId, "Antigo Sem Telefone");

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/vendas", new LancarVenda(
            [new ItemLancarVenda(produto, 1, 20m)], c.Agenda.ProfissionalId, null, NovoCliente: new NovoClienteVenda("Cliente Novo Da Venda", null)));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        _commit.Falhas.Should().Be(1);
        var venda = await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().SingleAsync(v => v.NegocioId == c.NegocioId));
        venda.ClienteId.Should().NotBe(antigo);
        (await NomeDoClienteAsync(venda.ClienteId!.Value)).Should().Be("Cliente Novo Da Venda");
        (await ClientesSemTelefoneAsync(c.NegocioId)).Should().BeEquivalentTo(["Antigo Sem Telefone", "Cliente Novo Da Venda"]);
    }

    [Fact]
    public async Task Venda_sem_falha_continua_igual_e_cada_requisicao_legitima_gera_uma_venda_nova()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);
        var pedido = new LancarVenda([new ItemLancarVenda(produto, 2, 20m)], c.Agenda.ProfissionalId, null);

        _commit.Desarmar();
        var primeira = await c.Admin.PostAsJsonAsync("/painel/vendas", pedido);
        var segunda = await c.Admin.PostAsJsonAsync("/painel/vendas", pedido);

        primeira.StatusCode.Should().Be(HttpStatusCode.Created);
        segunda.StatusCode.Should().Be(HttpStatusCode.Created);
        _commit.Falhas.Should().Be(0);
        var ids = new[] { (await primeira.Content.ReadFromJsonAsync<VendaLancada>())!.VendaId, (await segunda.Content.ReadFromJsonAsync<VendaLancada>())!.VendaId };
        ids.Distinct().Should().HaveCount(2, "outra requisição recebe outro identificador, mesmo com o conteúdo idêntico");
        (await EstoqueAsync(produto)).Should().Be((6, 6, 3));
        (await AuditoriaAsync(c, AcoesAuditoria.VendaProduto)).Should().Be(2);
    }

    [Fact]
    public async Task Requisicao_nova_depois_de_uma_com_commit_ambiguo_cria_outra_venda()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);
        var pedido = new LancarVenda([new ItemLancarVenda(produto, 2, 20m)], c.Agenda.ProfissionalId, null);

        _commit.ArmarCommitAmbiguo();
        var primeira = await c.Admin.PostAsJsonAsync("/painel/vendas", pedido);
        _commit.Desarmar();
        var segunda = await c.Admin.PostAsJsonAsync("/painel/vendas", pedido);

        (await primeira.Content.ReadFromJsonAsync<VendaLancada>())!.VendaId
            .Should().NotBe((await segunda.Content.ReadFromJsonAsync<VendaLancada>())!.VendaId);
        (await EstoqueAsync(produto)).Should().Be((6, 6, 3), "duas vendas de verdade, cada uma aplicada uma vez");
    }

    [Fact]
    public async Task Venda_com_falha_antes_do_commit_e_repetida_e_grava_uma_vez()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        _commit.ArmarFalhaAntesDoCommit();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/vendas", new LancarVenda([new ItemLancarVenda(produto, 2, 20m)], c.Agenda.ProfissionalId, null));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        _commit.Falhas.Should().Be(1);
        _commit.Commits.Should().Be(1, "só a segunda tentativa chegou a confirmar");
        (await EstoqueAsync(produto)).Should().Be((8, 8, 2));
        (await VendasAsync(c)).Should().HaveCount(1);
        (await AuditoriaAsync(c, AcoesAuditoria.VendaProduto)).Should().Be(1);
    }

    [Fact]
    public async Task Venda_com_falha_definitiva_nao_vira_sucesso_e_nada_e_gravado()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        _commit.ArmarFalhaAntesDoCommit(transitoria: false);
        var resposta = await c.Admin.PostAsJsonAsync("/painel/vendas", new LancarVenda([new ItemLancarVenda(produto, 2, 20m)], c.Agenda.ProfissionalId, null));

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        _commit.Falhas.Should().Be(1, "falha que não é transitória não é repetida");
        (await VendasAsync(c)).Should().BeEmpty();
        (await EstoqueAsync(produto)).Should().Be((10, 10, 1));
        (await AuditoriaAsync(c, AcoesAuditoria.VendaProduto)).Should().Be(0);
    }

    // ------------------------------------------------------------------ estoque

    [Fact]
    public async Task Entrada_de_estoque_com_commit_confirmado_e_resposta_perdida_soma_uma_so_vez()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync($"/painel/estoque/produtos/{produto}/entradas", new RegistrarEntrada(5, 3m));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        _commit.Falhas.Should().Be(1);
        (await resposta.Content.ReadFromJsonAsync<ProdutoResumo>())!.QuantidadeEstoque.Should().Be(15);
        (await EstoqueAsync(produto)).Should().Be((15, 15, 2), "estoque inicial + uma entrada");
        (await AuditoriaAsync(c, AcoesAuditoria.EntradaEstoque)).Should().Be(1);
    }

    [Fact]
    public async Task Entrada_de_estoque_sem_falha_e_repetida_de_proposito_soma_duas_vezes()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        _commit.Desarmar();
        (await c.Admin.PostAsJsonAsync($"/painel/estoque/produtos/{produto}/entradas", new RegistrarEntrada(5, 3m))).EnsureSuccessStatusCode();
        (await c.Admin.PostAsJsonAsync($"/painel/estoque/produtos/{produto}/entradas", new RegistrarEntrada(5, 3m))).EnsureSuccessStatusCode();

        (await EstoqueAsync(produto)).Should().Be((20, 20, 3), "duas requisições legítimas, dois movimentos");
    }

    [Fact]
    public async Task Entrada_de_estoque_com_falha_antes_do_commit_e_repetida()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        _commit.ArmarFalhaAntesDoCommit();
        var resposta = await c.Admin.PostAsJsonAsync($"/painel/estoque/produtos/{produto}/entradas", new RegistrarEntrada(5, 3m));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        _commit.Falhas.Should().Be(1);
        (await EstoqueAsync(produto)).Should().Be((15, 15, 2));
    }

    [Fact]
    public async Task Ajuste_de_estoque_com_commit_confirmado_e_resposta_perdida_devolve_sucesso_em_vez_de_400()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync($"/painel/estoque/produtos/{produto}/ajustes", new RegistrarAjuste(7, "Contagem"));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EstoqueAsync(produto)).Should().Be((7, 7, 2));
    }

    // ------------------------------------------------------------------ consumo interno

    [Fact]
    public async Task Consumo_interno_com_commit_confirmado_e_resposta_perdida_aplica_estoque_e_debito_uma_vez()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        Guid? consumoId;
        using (var escopo = Escopo(c.NegocioId))
            consumoId = await escopo.ServiceProvider.GetRequiredService<IServicoSaldoDevedor>()
                .LancarConsumoAsync(produto, new LancarConsumo(c.Agenda.ProfissionalId, 2));

        _commit.Falhas.Should().Be(1);
        var lancamentos = await _fabrica.NoBancoAsync(db => db.LancamentosSaldoDevedor.IgnoreQueryFilters()
            .Where(l => l.NegocioId == c.NegocioId).Select(l => new { l.Id, l.Valor }).ToListAsync());
        lancamentos.Should().ContainSingle().Which.Id.Should().Be(consumoId!.Value);
        lancamentos[0].Valor.Should().Be(10m, "2 × custo 5,00, debitado uma vez");
        (await EstoqueAsync(produto)).Should().Be((8, 8, 2));
        (await AuditoriaAsync(c, AcoesAuditoria.LancarConsumoInterno)).Should().Be(1);
    }

    [Fact]
    public async Task Consumo_interno_pela_api_com_commit_ambiguo_responde_sucesso()
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync($"/painel/estoque/produtos/{produto}/consumos", new LancarConsumo(c.Agenda.ProfissionalId, 2));

        resposta.IsSuccessStatusCode.Should().BeTrue(await resposta.Content.ReadAsStringAsync());
        (await EstoqueAsync(produto)).Should().Be((8, 8, 2));
    }

    // ------------------------------------------------------------------ agenda

    [Fact]
    public async Task Agendamento_forcado_fora_do_expediente_com_commit_ambiguo_cria_um_so()
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 19); // fora do expediente: regra quebrada, agendamento forçado

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/agendamentos/forcados", Forcado(c, inicio));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        _commit.Falhas.Should().Be(1);
        var id = await resposta.Content.ReadFromJsonAsync<Guid>();
        var lista = await AgendamentosEmAsync(c, inicio);
        lista.Should().ContainSingle().Which.Id.Should().Be(id);
        lista[0].Forcado.Should().BeTrue();
        (await AuditoriaAsync(c, AcoesAuditoria.ForcarAgendamento)).Should().Be(1);
    }

    [Fact]
    public async Task Agendamento_forcado_sem_regra_quebrada_com_commit_ambiguo_cria_um_so()
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 10); // horário livre e dentro do expediente: vira agendamento comum

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/agendamentos/forcados", Forcado(c, inicio));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        _commit.Falhas.Should().Be(1);
        (await AgendamentosEmAsync(c, inicio)).Should().ContainSingle().Which.Id.Should().Be(await resposta.Content.ReadFromJsonAsync<Guid>());
    }

    [Fact]
    public async Task Agendamento_forcado_por_cima_de_outro_continua_criando_dois_quando_sao_duas_requisicoes()
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 10);
        await SemearAsync(c, 10);

        _commit.Desarmar();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/agendamentos/forcados", Forcado(c, inicio));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        (await AgendamentosEmAsync(c, inicio)).Should().HaveCount(2, "forçar sobre um horário ocupado é o propósito do recurso");
    }

    [Fact]
    public async Task Encaixe_forcado_fora_do_expediente_com_commit_ambiguo_cria_um_so()
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 19);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            c.Agenda.ProfissionalId, c.Agenda.ClienteId, null, [c.Agenda.ServicoId], inicio, false, false, null, "Encaixe especial"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        _commit.Falhas.Should().Be(1);
        var lista = await AgendamentosEmAsync(c, inicio);
        lista.Should().ContainSingle().Which.Forcado.Should().BeTrue();
        (await AuditoriaAsync(c, AcoesAuditoria.ForcarAgendamento)).Should().Be(1);
    }

    [Fact]
    public async Task Encaixe_forcado_sem_regra_quebrada_com_commit_ambiguo_cria_um_so()
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 11);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            c.Agenda.ProfissionalId, c.Agenda.ClienteId, null, [c.Agenda.ServicoId], inicio, false, false, null, "Encaixe especial"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        _commit.Falhas.Should().Be(1);
        (await AgendamentosEmAsync(c, inicio)).Should().ContainSingle("antes da B7.1 a repetição via o 1º como sobreposição e forçava um 2º");
    }

    [Fact]
    public async Task Encaixe_com_cliente_novo_sem_telefone_fica_com_o_cliente_certo_mesmo_havendo_outro_sem_telefone()
    {
        var c = await ArranjarAsync();
        var antigo = await CriarClienteSemTelefoneAsync(c.NegocioId, "Antigo Sem Telefone");
        var inicio = Local(c, 11);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            c.Agenda.ProfissionalId, null, new NovoClienteEncaixe("Cliente Novo Do Encaixe", null), [c.Agenda.ServicoId], inicio, false, false));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        _commit.Falhas.Should().Be(1);
        var agendamento = (await AgendamentosEmAsync(c, inicio)).Should().ContainSingle().Subject;
        agendamento.ClienteId.Should().NotBe(antigo);
        (await NomeDoClienteAsync(agendamento.ClienteId!.Value)).Should().Be("Cliente Novo Do Encaixe");
    }

    [Fact]
    public async Task Encaixe_comum_com_commit_ambiguo_responde_sucesso_e_nao_conflito()
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 11);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            c.Agenda.ProfissionalId, c.Agenda.ClienteId, null, [c.Agenda.ServicoId], inicio, false, false));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created, "antes da B7.1 a repetição batia na exclusion constraint e virava 409");
        (await AgendamentosEmAsync(c, inicio)).Should().ContainSingle();
    }

    [Fact]
    public async Task Criacao_comum_pelo_painel_com_commit_ambiguo_responde_201_com_o_id_gravado()
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 10);

        _commit.ArmarCommitAmbiguo();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(c.Agenda.ProfissionalId, c.Agenda.ClienteId, [c.Agenda.ServicoId], inicio));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        (await AgendamentosEmAsync(c, inicio)).Should().ContainSingle();
    }

    [Fact]
    public async Task Reserva_publica_com_commit_ambiguo_devolve_sucesso_com_a_reserva_gravada()
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 11);

        _commit.ArmarCommitAmbiguo();
        ResultadoAgendamento resultado;
        using (var escopo = Escopo(c.NegocioId))
            resultado = await escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>()
                .CriarReservaPublicaAsync(new CriarReservaPublica(c.Agenda.ProfissionalId, [c.Agenda.ServicoId], inicio));

        resultado.Sucesso.Should().BeTrue(resultado.MensagemErro);
        var lista = await AgendamentosEmAsync(c, inicio);
        lista.Should().ContainSingle().Which.Id.Should().Be(resultado.AgendamentoId!.Value);
        lista[0].Status.Should().Be(StatusAgendamento.Reservado);
    }

    [Fact]
    public async Task Horario_realmente_ocupado_por_outra_pessoa_continua_dando_conflito_mesmo_com_o_novo_fluxo()
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 10);
        await SemearAsync(c, 10);

        _commit.Desarmar();
        var resposta = await c.Admin.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(c.Agenda.ProfissionalId, c.Agenda.ClienteId, [c.Agenda.ServicoId], inicio));

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AgendamentosEmAsync(c, inicio)).Should().ContainSingle();
    }

    // ------------------------------------------------------------------ tenant

    [Fact]
    public async Task Commit_ambiguo_de_um_negocio_nao_toca_nem_enxerga_dados_de_outro_negocio()
    {
        var a = await ArranjarAsync();
        var b = await ArranjarAsync();
        var produtoA = await CriarProdutoAsync(a, 10);
        var produtoB = await CriarProdutoAsync(b, 10);
        var antigoB = await CriarClienteSemTelefoneAsync(b.NegocioId, "Antigo Do Outro Negocio");

        // O negócio B já fez uma venda e um encaixe; o A sofre o COMMIT ambíguo.
        _commit.Desarmar();
        (await b.Admin.PostAsJsonAsync("/painel/vendas", new LancarVenda([new ItemLancarVenda(produtoB, 1, 20m)], b.Agenda.ProfissionalId, null))).EnsureSuccessStatusCode();
        var inicioA = Local(a, 11);
        _commit.ArmarCommitAmbiguo();
        var venda = await a.Admin.PostAsJsonAsync("/painel/vendas", new LancarVenda([new ItemLancarVenda(produtoA, 2, 20m)], a.Agenda.ProfissionalId, null));
        _commit.ArmarCommitAmbiguo();
        var encaixe = await a.Admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            a.Agenda.ProfissionalId, null, new NovoClienteEncaixe("Cliente Novo Do A", null), [a.Agenda.ServicoId], inicioA, false, false));

        venda.StatusCode.Should().Be(HttpStatusCode.Created);
        encaixe.StatusCode.Should().Be(HttpStatusCode.Created);
        (await VendasAsync(a)).Should().HaveCount(1);
        (await VendasAsync(b)).Should().HaveCount(1);
        (await EstoqueAsync(produtoA)).Should().Be((8, 8, 2));
        (await EstoqueAsync(produtoB)).Should().Be((9, 9, 2));
        var agendamentoA = (await AgendamentosEmAsync(a, inicioA)).Should().ContainSingle().Subject;
        agendamentoA.ClienteId.Should().NotBe(antigoB);
        (await _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().Where(x => x.Id == agendamentoA.ClienteId).Select(x => x.NegocioId).SingleAsync()))
            .Should().Be(a.NegocioId);
    }

    // ------------------------------------------------------------------ apoio

    private sealed record Cenario(Guid NegocioId, HttpClient Admin, SemeadorDeAgenda.CenarioDeAgenda Agenda, DateOnly Segunda);

    private async Task<Cenario> ArranjarAsync()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador,
            Permissao.ForcarAgendamento, Permissao.GerenciarEstoque, Permissao.VenderProdutos, Permissao.LancarVales);
        var agenda = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);
        return new Cenario(negocioId, admin, agenda, segunda);
    }

    private static DateTimeOffset Local(Cenario c, int hora) => SemeadorDeComissoes.Local(c.Segunda, hora, 0);

    private static object Forcado(Cenario c, DateTimeOffset inicio) => new
    {
        profissionalId = c.Agenda.ProfissionalId, clienteId = c.Agenda.ClienteId, servicoIds = new[] { c.Agenda.ServicoId },
        inicio, observacoes = (string?)null, motivo = "Cliente especial",
    };

    private Task<Guid> SemearAsync(Cenario c, int hora) =>
        _fabrica.SemearAgendamentoAsync(c.NegocioId, c.Agenda.ProfissionalId, Local(c, hora), precos: [50m], servicoId: c.Agenda.ServicoId);

    private async Task<Guid> CriarProdutoAsync(Cenario c, int quantidade)
    {
        var resposta = await c.Admin.PostAsJsonAsync("/painel/estoque/produtos", new CriarProduto("Pomada B7", null, 5m, 20m, quantidade));
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    private Task<Guid> CriarClienteSemTelefoneAsync(Guid negocioId, string nome) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var cliente = Cliente.Criar(negocioId, nome, null);
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();
            return cliente.Id;
        });

    private Task<string> NomeDoClienteAsync(Guid id) =>
        _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().Where(x => x.Id == id).Select(x => x.Nome).SingleAsync());

    private Task<List<string>> ClientesSemTelefoneAsync(Guid negocioId) =>
        _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().Where(x => x.NegocioId == negocioId && x.Telefone == null).Select(x => x.Nome).ToListAsync());

    /// <summary>(quantidade do produto, soma dos movimentos, nº de movimentos): a soma tem de bater com a quantidade.</summary>
    private Task<(int, int, int)> EstoqueAsync(Guid produto) =>
        _fabrica.NoBancoAsync(async db => (
            await db.Produtos.IgnoreQueryFilters().Where(p => p.Id == produto).Select(p => p.QuantidadeEstoque).SingleAsync(),
            await db.MovimentosEstoque.IgnoreQueryFilters().Where(m => m.ProdutoId == produto).SumAsync(m => m.Quantidade),
            await db.MovimentosEstoque.IgnoreQueryFilters().CountAsync(m => m.ProdutoId == produto)));

    private Task<List<Guid>> VendasAsync(Cenario c) =>
        _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().Where(v => v.NegocioId == c.NegocioId).Select(v => v.Id).ToListAsync());

    private Task<List<Agendamento>> AgendamentosEmAsync(Cenario c, DateTimeOffset inicio) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.ProfissionalId == c.Agenda.ProfissionalId && a.Inicio == inicio).ToListAsync());

    private Task<int> AuditoriaAsync(Cenario c, string acao) =>
        _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.NegocioId == c.NegocioId && l.Acao == acao));

    private IServiceScope Escopo(Guid negocioId)
    {
        var escopo = _fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioId);
        return escopo;
    }
}
