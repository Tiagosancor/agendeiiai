using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comissoes;
using Plataforma.Dominio.Estoque;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// B7.1 — o caso real: um proxy TCP entre a API e o PostgreSQL repassa o pedido (o servidor executa e confirma) e derruba a
/// conexão antes da resposta voltar. Diferente do interceptador, aqui é o Npgsql de verdade que falha com
/// <c>IOException</c> e a <c>ExecutionStrategy</c> de verdade que decide repetir.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class CommitAmbiguoProxyTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private ProxyQuePerdeResposta _proxy = null!;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public CommitAmbiguoProxyTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        // Pools de fábricas anteriores da suíte seguram conexões do Postgres compartilhado; aqui cada conexão conta.
        NpgsqlConnection.ClearAllPools();
        var real = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString);
        _proxy = new ProxyQuePerdeResposta(real.Host!, real.Port);
        var viaProxy = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString)
        {
            Host = "127.0.0.1", Port = _proxy.Porta, Pooling = false,
        }.ConnectionString;
        _fabrica = new PlataformaWebApplicationFactory(viaProxy);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync()
    {
        await _fabrica.DisposeAsync();
        _proxy.Dispose();
        // Sem pool, cada conexão do teste é fechada ao devolver; limpa também os pools de fábricas anteriores da suíte.
        NpgsqlConnection.ClearAllPools();
    }

    [Fact]
    public async Task Venda_com_conexao_derrubada_no_commit_grava_uma_so_vez()
    {
        var (admin, negocioId, agenda) = await ArranjarAsync();
        var produto = await CriarProdutoAsync(admin, 10);

        _proxy.Armar("INSERT INTO vendas_produto", "COMMIT");
        var resposta = await admin.PostAsJsonAsync("/painel/vendas", new LancarVenda([new ItemLancarVenda(produto, 2, 20m)], agenda.ProfissionalId, null));

        _proxy.Derrubadas.Should().Be(1, "a conexão da venda precisa ter sido derrubada no COMMIT, senão o teste não prova nada");
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var lancada = (await resposta.Content.ReadFromJsonAsync<VendaLancada>())!;
        var vendas = await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().Where(v => v.NegocioId == negocioId)
            .Select(v => new { v.Id, v.Total }).ToListAsync());
        vendas.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = lancada.VendaId, Total = 40m });
        (await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().Where(p => p.Id == produto).Select(p => p.QuantidadeEstoque).SingleAsync()))
            .Should().Be(8);
    }

    [Fact]
    public async Task Entrada_de_estoque_com_conexao_derrubada_no_commit_soma_uma_so_vez()
    {
        var (admin, _, _) = await ArranjarAsync();
        var produto = await CriarProdutoAsync(admin, 10);

        _proxy.Armar("INSERT INTO movimentos_estoque", "COMMIT");
        var resposta = await admin.PostAsJsonAsync($"/painel/estoque/produtos/{produto}/entradas", new RegistrarEntrada(5, 3m));

        _proxy.Derrubadas.Should().Be(1);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().Where(p => p.Id == produto).Select(p => p.QuantidadeEstoque).SingleAsync()))
            .Should().Be(15);
        (await _fabrica.NoBancoAsync(db => db.MovimentosEstoque.IgnoreQueryFilters().CountAsync(m => m.ProdutoId == produto))).Should().Be(2);
    }

    [Fact]
    public async Task Encaixe_com_cliente_novo_sem_telefone_e_insert_avulso_derrubado_fica_com_o_cliente_certo()
    {
        var (admin, negocioId, agenda) = await ArranjarAsync();
        var antigo = await _fabrica.NoBancoAsync(async db =>
        {
            var cliente = Cliente.Criar(negocioId, "Antigo Sem Telefone", null);
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();
            return cliente.Id;
        });
        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);
        var inicio = SemeadorDeComissoes.Local(segunda, 11, 0);

        _proxy.Armar("INSERT INTO clientes");
        var resposta = await admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            agenda.ProfissionalId, null, new NovoClienteEncaixe("Cliente Novo Do Encaixe", null), [agenda.ServicoId], inicio, false, false));

        _proxy.Derrubadas.Should().Be(1);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var agendamentos = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().Where(a => a.NegocioId == negocioId).ToListAsync());
        var agendamento = agendamentos.Should().ContainSingle().Subject;
        agendamento.ClienteId.Should().NotBe(antigo);
        (await _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().Where(c => c.Id == agendamento.ClienteId).Select(c => c.Nome).SingleAsync()))
            .Should().Be("Cliente Novo Do Encaixe");
        (await _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().CountAsync(c => c.NegocioId == negocioId && c.Telefone == null)))
            .Should().Be(2, "o antigo e o novo; o novo não é duplicado nem trocado");
    }

    [Fact]
    public async Task Venda_com_cliente_novo_sem_telefone_e_insert_avulso_derrubado_fica_com_o_cliente_certo()
    {
        var (admin, negocioId, agenda) = await ArranjarAsync();
        var produto = await CriarProdutoAsync(admin, 10);
        var antigo = await _fabrica.NoBancoAsync(async db =>
        {
            var cliente = Cliente.Criar(negocioId, "Antigo Sem Telefone", null);
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();
            return cliente.Id;
        });

        _proxy.Armar("INSERT INTO clientes");
        var resposta = await admin.PostAsJsonAsync("/painel/vendas", new LancarVenda(
            [new ItemLancarVenda(produto, 1, 20m)], agenda.ProfissionalId, null, NovoCliente: new NovoClienteVenda("Cliente Novo Da Venda", null)));

        _proxy.Derrubadas.Should().Be(1);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var venda = await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().SingleAsync(v => v.NegocioId == negocioId));
        venda.ClienteId.Should().NotBe(antigo);
        (await _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().Where(c => c.Id == venda.ClienteId).Select(c => c.Nome).SingleAsync()))
            .Should().Be("Cliente Novo Da Venda");
    }

    // ------------------------------------------------------------------ B7.2

    [Fact]
    public async Task Cancelar_com_conexao_derrubada_no_commit_responde_204_e_avisa_o_profissional_uma_vez()
    {
        var (admin, negocioId, agenda) = await ArranjarAsync();
        const string emailProf = "prof.proxy.b72@teste.com";
        await _fabrica.NoBancoAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE profissionais SET email = {emailProf} WHERE id = {agenda.ProfissionalId}"));
        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);
        var id = await _fabrica.SemearAgendamentoAsync(negocioId, agenda.ProfissionalId, SemeadorDeComissoes.Local(segunda, 10, 0),
            precos: [50m], servicoId: agenda.ServicoId);
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espia.Enviados.Clear();

        _proxy.Armar("UPDATE agendamentos", "COMMIT");
        var resposta = await admin.PostAsync($"/painel/agendamentos/{id}/cancelar", null);

        _proxy.Derrubadas.Should().Be(1, "a conexão do cancelamento precisa ter sido derrubada no COMMIT");
        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().Where(a => a.Id == id).Select(a => a.Status).SingleAsync()))
            .Should().Be(StatusAgendamento.Cancelado);
        espia.Enviados.Count(e => e.Destinatario == emailProf).Should().Be(1);
    }

    [Fact]
    public async Task Vale_com_conexao_derrubada_no_commit_responde_sucesso_e_grava_um_so()
    {
        var (admin, negocioId, agenda) = await ArranjarAsync();

        _proxy.Armar("INSERT INTO lancamentos_saldo_devedor", "COMMIT");
        var resposta = await admin.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(agenda.ProfissionalId, 30m));

        _proxy.Derrubadas.Should().Be(1);
        resposta.IsSuccessStatusCode.Should().BeTrue(await resposta.Content.ReadAsStringAsync());
        var vales = await _fabrica.NoBancoAsync(db => db.LancamentosSaldoDevedor.IgnoreQueryFilters()
            .Where(l => l.NegocioId == negocioId).Select(l => new { l.Id, l.Valor }).ToListAsync());
        vales.Should().ContainSingle().Which.Valor.Should().Be(30m);
        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .CountAsync(l => l.NegocioId == negocioId && l.EntidadeId == vales[0].Id))).Should().Be(1);
    }

    [Fact]
    public async Task Produto_com_conexao_derrubada_no_commit_responde_sucesso_com_estoque_inicial_uma_vez()
    {
        var (admin, negocioId, _) = await ArranjarAsync();

        _proxy.Armar("INSERT INTO produtos", "COMMIT");
        var resposta = await admin.PostAsJsonAsync("/painel/estoque/produtos", new CriarProduto("Pomada Proxy B72", null, 5m, 20m, 10));

        _proxy.Derrubadas.Should().Be(1);
        resposta.IsSuccessStatusCode.Should().BeTrue(await resposta.Content.ReadAsStringAsync());
        var id = await resposta.Content.ReadFromJsonAsync<Guid>();
        var produtos = await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().Where(p => p.NegocioId == negocioId).Select(p => new { p.Id, p.QuantidadeEstoque }).ToListAsync());
        produtos.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = id, QuantidadeEstoque = 10 });
        (await _fabrica.NoBancoAsync(db => db.MovimentosEstoque.IgnoreQueryFilters().CountAsync(m => m.ProdutoId == id))).Should().Be(1);
    }

    // ------------------------------------------------------------------ B7.3

    [Fact]
    public async Task Iniciar_com_conexao_derrubada_no_commit_responde_204_e_deixa_em_atendimento()
    {
        var (admin, negocioId, agenda) = await ArranjarAsync();
        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);
        var id = await _fabrica.SemearAgendamentoAsync(negocioId, agenda.ProfissionalId, SemeadorDeComissoes.Local(segunda, 10, 0),
            precos: [50m], servicoId: agenda.ServicoId);

        _proxy.Armar("UPDATE agendamentos", "COMMIT");
        var resposta = await admin.PostAsync($"/painel/agendamentos/{id}/iniciar", null);

        _proxy.Derrubadas.Should().Be(1, "a conexão precisa ter sido derrubada no COMMIT");
        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().Where(a => a.Id == id).Select(a => a.Status).SingleAsync()))
            .Should().Be(StatusAgendamento.EmAtendimento);
    }

    private async Task<(HttpClient Admin, Guid NegocioId, SemeadorDeAgenda.CenarioDeAgenda Agenda)> ArranjarAsync()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.GerenciarEstoque, Permissao.VenderProdutos, Permissao.LancarVales);
        var agenda = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        return (admin, negocioId, agenda);
    }

    private static async Task<Guid> CriarProdutoAsync(HttpClient admin, int quantidade)
    {
        var resposta = await admin.PostAsJsonAsync("/painel/estoque/produtos", new CriarProduto("Pomada Proxy", null, 5m, 20m, quantidade));
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }
}
