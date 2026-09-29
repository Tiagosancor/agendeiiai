using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Comissoes;
using Plataforma.Dominio.Estoque;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.Infraestrutura.SemeadorDeComissoes;

namespace Plataforma.Testes.Integracao.Comissoes;

/// <summary>Vale, consumo interno, saldo devedor e quitação no fechamento da quinzena (seção 7, item 12 d/e/g da seção 14).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class SaldoDevedorTestes : IAsyncLifetime
{
    private const string Produtos = "/painel/estoque/produtos";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public SaldoDevedorTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Consumo_interno_baixa_o_estoque_pelo_custo_vira_saldo_devedor_e_nao_entra_no_faturamento()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await _fabrica.CriarProfissionalAsync(negocioId, "Ana");
        var produtoId = await CriarProdutoAsync(admin, "Gel", custo: 8m, venda: 25m, quantidade: 3);

        var consumo = await admin.PostAsJsonAsync($"{Produtos}/{produtoId}/consumos", new LancarConsumo(profissionalId, 2));
        consumo.StatusCode.Should().Be(HttpStatusCode.Created);

        // Custo por padrão (decisão do dono), editável na hora.
        (await admin.PostAsJsonAsync($"{Produtos}/{produtoId}/consumos", new LancarConsumo(profissionalId, 1, 6.5m, "Uso no salão")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var maisQueOEstoque = await admin.PostAsJsonAsync($"{Produtos}/{produtoId}/consumos", new LancarConsumo(profissionalId, 1));
        maisQueOEstoque.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await maisQueOEstoque.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("disponivel").GetInt32().Should().Be(0);

        var movimentos = await _fabrica.NoBancoAsync(db => db.MovimentosEstoque.IgnoreQueryFilters()
            .Where(m => m.ProdutoId == produtoId).ToListAsync());
        movimentos.Where(m => m.Tipo == TipoMovimentoEstoque.ConsumoInterno).Select(m => m.Quantidade).Should().BeEquivalentTo([-2, -1]);
        movimentos.Sum(m => m.Quantidade).Should().Be(0);

        var saldo = (await admin.GetFromJsonAsync<SaldoDevedor>($"/painel/saldos/profissionais/{profissionalId}"))!;
        saldo.ConsumoEmAberto.Should().Be(22.50m); // 2 × 8,00 + 1 × 6,50
        saldo.ValesEmAberto.Should().Be(0m);
        saldo.Lancamentos.Should().HaveCount(2).And.OnlyContain(l => l.Produto == "Gel");

        var hoje = Dia(0).ToString("yyyy-MM-dd");
        var resumo = (await admin.GetFromJsonAsync<ResumoFinanceiro>($"/painel/financeiro/resumo?inicio={hoje}&fim={hoje}"))!;
        resumo.Total.Should().Be(0m);
        resumo.TotalProdutos.Should().Be(0m);

        // Excluir o consumo devolve o produto ao estoque, por movimento.
        var consumoId = await consumo.Content.ReadFromJsonAsync<Guid>();
        (await admin.DeleteAsync($"/painel/saldos/{consumoId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().SingleAsync(p => p.Id == produtoId))).QuantidadeEstoque.Should().Be(2);
        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .CountAsync(l => l.Acao == "LancarConsumoInterno" || l.Acao == "ExcluirLancamentoSaldo"))).Should().Be(3);
    }

    [Fact]
    public async Task Vale_e_consumo_exigem_as_permissoes_e_o_profissional_so_ve_o_proprio_saldo()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await _fabrica.CriarProfissionalAsync(negocioId, "Beto");
        var outroId = await _fabrica.CriarProfissionalAsync(negocioId, "Caio");
        var produtoId = await CriarProdutoAsync(admin, "Cera", custo: 5m, venda: 15m, quantidade: 5);

        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista,
            permissoesExtras: Usuario.PermissoesPadrao(Perfil.Recepcionista).ToArray());
        (await recepcao.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(profissionalId, 50m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.PostAsJsonAsync($"{Produtos}/{produtoId}/consumos", new LancarConsumo(profissionalId, 1)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.GetAsync("/painel/saldos")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await admin.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(profissionalId, 0m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var vale = await admin.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(profissionalId, 50m, Dia(-1), "Adiantamento"));
        vale.StatusCode.Should().Be(HttpStatusCode.Created);
        var valeId = await vale.Content.ReadFromJsonAsync<Guid>();
        await admin.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(outroId, 30m));

        // Só "lançar vales" (sem estoque) mexe em vale, mas não em consumo.
        using var soVales = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista, permissoesExtras: Permissao.LancarVales);
        (await soVales.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(profissionalId, 10m))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await soVales.PostAsJsonAsync($"{Produtos}/{produtoId}/consumos", new LancarConsumo(profissionalId, 1)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // O profissional vê só o dele, e só lê.
        using var profissional = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, profissionalId);
        var meu = (await profissional.GetFromJsonAsync<SaldoDevedor>("/painel/comissoes/minhas/saldo"))!;
        meu.ProfissionalId.Should().Be(profissionalId);
        meu.ValesEmAberto.Should().Be(60m);
        meu.Lancamentos.Should().ContainSingle(l => l.Descricao == "Adiantamento");
        (await profissional.GetAsync($"/painel/saldos/profissionais/{outroId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await profissional.PutAsJsonAsync($"/painel/saldos/{valeId}", new AlterarLancamentoSaldo(Valor: 1m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await profissional.DeleteAsync($"/painel/saldos/{valeId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Sem vínculo com profissional, não há saldo.
        (await recepcao.GetAsync("/painel/comissoes/minhas/saldo")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await admin.PutAsJsonAsync($"/painel/saldos/{valeId}", new AlterarLancamentoSaldo(Valor: 45m, Data: Dia(-1), Descricao: "Adiantamento")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var resumo = await admin.GetFromJsonAsync<List<SaldoDoProfissional>>("/painel/saldos");
        resumo!.Single(s => s.ProfissionalId == profissionalId).ValesEmAberto.Should().Be(55m);
        resumo.Single(s => s.ProfissionalId == outroId).TotalEmAberto.Should().Be(30m);
    }

    [Fact]
    public async Task Fechamento_desconta_do_mais_antigo_nunca_deixa_o_liquido_negativo_e_reabrir_devolve_para_em_aberto()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await _fabrica.CriarProfissionalAsync(negocioId, "Duda");
        (await admin.PutAsJsonAsync($"/painel/profissionais/{profissionalId}/comissao", new ConfiguracaoComissao(10m, true, 10m)))
            .EnsureSuccessStatusCode();
        using var profissional = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, profissionalId);

        // Comissão bruta: 10% de R$ 50 (serviço) + 10% de R$ 20 (produto vendido por ela) = R$ 7,00.
        await _fabrica.SemearEConcluirAsync(admin, negocioId, profissionalId, Local(Dia(-1), 10, 0));
        var produtoId = await CriarProdutoAsync(admin, "Tônico", custo: 5m, venda: 20m, quantidade: 5);
        (await admin.PostAsJsonAsync("/painel/vendas", new LancarVenda([new ItemLancarVenda(produtoId, 1, 20m)], profissionalId, null)))
            .EnsureSuccessStatusCode();

        // Saldo devedor: vale de R$ 4 (mais antigo), consumo de R$ 5 e um vale depois do fim da quinzena (não entra).
        var vale = await (await admin.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(profissionalId, 4m, Dia(-2))))
            .Content.ReadFromJsonAsync<Guid>();
        (await admin.PostAsJsonAsync($"{Produtos}/{produtoId}/consumos", new LancarConsumo(profissionalId, 1))).EnsureSuccessStatusCode();
        await admin.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(profissionalId, 100m, Dia(1)));

        var quinzena = await CriarQuinzenaAsync(admin, Dia(-3), Dia(0));
        var parcial = (await admin.GetFromJsonAsync<DetalheQuinzena>($"/painel/quinzenas/{quinzena}"))!.Linhas.Single();
        (parcial.Totais.TotalComissao, parcial.ComissaoProdutos, parcial.Vales, parcial.Consumo, parcial.Liquido, parcial.SaldoRestante)
            .Should().Be((5m, 2m, 4m, 3m, 0m, 2m));

        (await admin.PostAsJsonAsync($"/painel/quinzenas/{quinzena}/fechar", new FecharQuinzenaRequisicao(false)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var fechamento = await _fabrica.NoBancoAsync(db => db.FechamentosComissao.IgnoreQueryFilters().SingleAsync());
        (fechamento.TotalComissao, fechamento.TotalComissaoProdutos, fechamento.TotalVales, fechamento.TotalConsumo, fechamento.Liquido, fechamento.SaldoRestante)
            .Should().Be((5m, 2m, 4m, 3m, 0m, 2m));

        // Sobrou R$ 2 do consumo para a próxima quinzena, além do vale posterior.
        var saldo = (await admin.GetFromJsonAsync<SaldoDevedor>($"/painel/saldos/profissionais/{profissionalId}"))!;
        (saldo.ValesEmAberto, saldo.ConsumoEmAberto).Should().Be((100m, 2m));

        var minhas = (await profissional.GetFromJsonAsync<QuinzenasDoProfissional>("/painel/comissoes/minhas/quinzenas"))!;
        minhas.Quinzenas.Single().Should().Match<QuinzenaDoProfissional>(q => !q.Parcial && q.Liquido == 0m && q.SaldoRestante == 2m && q.Vales == 4m);

        // Mexer num lançamento já quitado pede confirmação e muda o fechamento passado.
        var semConfirmar = await admin.DeleteAsync($"/painel/saldos/{vale}");
        semConfirmar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await semConfirmar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("quitado");
        (await admin.DeleteAsync($"/painel/saldos/{vale}?confirmarQuitado=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var corrigido = await _fabrica.NoBancoAsync(db => db.FechamentosComissao.IgnoreQueryFilters().SingleAsync());
        (corrigido.TotalVales, corrigido.Liquido).Should().Be((0m, 4m));

        // Reabrir a quinzena devolve tudo o que foi descontado para "em aberto".
        (await admin.PostAsJsonAsync($"/painel/quinzenas/{quinzena}/reabrir", new ReabrirQuinzenaRequisicao("Conferência")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _fabrica.NoBancoAsync(db => db.QuitacoesSaldo.IgnoreQueryFilters().AnyAsync())).Should().BeFalse();
        saldo = (await admin.GetFromJsonAsync<SaldoDevedor>($"/painel/saldos/profissionais/{profissionalId}"))!;
        (saldo.ValesEmAberto, saldo.ConsumoEmAberto).Should().Be((100m, 5m));
    }

    [Fact]
    public async Task Recepcionista_sem_cadastro_de_profissional_entra_na_quinzena_com_comissao_de_produto_menos_vale_e_consumo()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista,
            permissoesExtras: Usuario.PermissoesPadrao(Perfil.Recepcionista).ToArray());
        var recepcaoId = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters()
            .Where(u => u.NegocioId == negocioId && u.Perfil == Perfil.Recepcionista).Select(u => u.Id).SingleAsync());

        // Sem acerto por quinzena ainda: sem saldo nem quinzenas para ela.
        (await recepcao.GetAsync("/painel/comissoes/minhas/saldo")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"/painel/usuarios/{recepcaoId}/comissao-produto", new DefinirPercentualProduto(10m, true)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<PercentualComissaoProdutoUsuario>($"/painel/usuarios/{recepcaoId}/comissao-produto"))!
            .AcertoPorQuinzena.Should().BeTrue();

        // Comissão de produto: 10% de 2 × R$ 30 = R$ 6. Saldo devedor: vale de R$ 4 (mais antigo) e consumo de R$ 5.
        var produtoId = await CriarProdutoAsync(admin, "Balm", custo: 5m, venda: 30m, quantidade: 5);
        var venda = await admin.PostAsJsonAsync("/painel/vendas", new LancarVenda([new ItemLancarVenda(produtoId, 2, 30m)], null, recepcaoId));
        venda.StatusCode.Should().Be(HttpStatusCode.Created);
        var vendaId = (await venda.Content.ReadFromJsonAsync<VendaLancada>())!.VendaId;
        (await admin.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(null, 4m, Dia(-1), "Adiantamento", recepcaoId)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync($"{Produtos}/{produtoId}/consumos", new LancarConsumo(null, 1, UsuarioId: recepcaoId)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Os dois, ou nenhum: recusado.
        var profissionalId = await _fabrica.CriarProfissionalAsync(negocioId, "Ana");
        (await admin.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(profissionalId, 4m, UsuarioId: recepcaoId)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/painel/saldos/vales", new LancarVale(null, 4m))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var resumo = (await admin.GetFromJsonAsync<List<SaldoDoProfissional>>("/painel/saldos"))!;
        resumo.Single(s => s.UsuarioId == recepcaoId).Should().Match<SaldoDoProfissional>(s =>
            s.ProfissionalId == null && s.ValesEmAberto == 4m && s.ConsumoEmAberto == 5m);

        var quinzena = await CriarQuinzenaAsync(admin, Dia(-3), Dia(0));
        var linha = (await admin.GetFromJsonAsync<DetalheQuinzena>($"/painel/quinzenas/{quinzena}"))!.Linhas.Single(l => l.UsuarioId == recepcaoId);
        (linha.ProfissionalId, linha.Totais.TotalComissao, linha.ComissaoProdutos, linha.Vales, linha.Consumo, linha.Liquido, linha.SaldoRestante)
            .Should().Be(((Guid?)null, 0m, 6m, 4m, 2m, 0m, 3m));

        (await admin.PostAsJsonAsync($"/painel/quinzenas/{quinzena}/fechar", new FecharQuinzenaRequisicao(false)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var fechamento = await _fabrica.NoBancoAsync(db => db.FechamentosComissao.IgnoreQueryFilters().SingleAsync(f => f.UsuarioId == recepcaoId));
        (fechamento.ProfissionalId, fechamento.TotalComissaoProdutos, fechamento.Liquido, fechamento.SaldoRestante)
            .Should().Be(((Guid?)null, 6m, 0m, 3m));
        var fechada = (await admin.GetFromJsonAsync<DetalheQuinzena>($"/painel/quinzenas/{quinzena}"))!.Linhas.Single(l => l.UsuarioId == recepcaoId);
        fechada.Nome.Should().NotBeEmpty();

        // Ela vê as próprias quinzenas e o saldo, só leitura.
        var minhas = (await recepcao.GetFromJsonAsync<QuinzenasDoProfissional>("/painel/comissoes/minhas/quinzenas"))!;
        minhas.AcertoPorQuinzena.Should().BeTrue();
        minhas.Quinzenas.Single().Should().Match<QuinzenaDoProfissional>(q => !q.Parcial && q.ComissaoProdutos == 6m && q.SaldoRestante == 3m);
        var meu = (await recepcao.GetFromJsonAsync<SaldoDevedor>("/painel/comissoes/minhas/saldo"))!;
        (meu.UsuarioId, meu.ValesEmAberto, meu.ConsumoEmAberto).Should().Be(((Guid?)recepcaoId, 0m, 3m));
        (await admin.GetFromJsonAsync<SaldoDevedor>($"/painel/saldos/usuarios/{recepcaoId}"))!.ConsumoEmAberto.Should().Be(3m);

        // A comissão da venda já entrou no fechamento: estornar só depois de reabrir.
        var estorno = await admin.PostAsJsonAsync($"/painel/vendas/{vendaId}/estornar", new EstornarVenda("Engano"));
        estorno.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await estorno.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("quinzena_fechada");

        // Com histórico de dinheiro, excluir o usuário é lógico (a linha fica para o fechamento).
        (await admin.DeleteAsync($"/painel/usuarios/{recepcaoId}")).EnsureSuccessStatusCode();
        (await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Id == recepcaoId))).Should().BeTrue();
    }

    private static async Task<Guid> CriarProdutoAsync(HttpClient admin, string nome, decimal custo, decimal venda, int quantidade)
    {
        var resposta = await admin.PostAsJsonAsync(Produtos, new CriarProduto(nome, null, custo, venda, quantidade));
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<Guid> CriarQuinzenaAsync(HttpClient admin, DateOnly inicio, DateOnly fim)
    {
        var resposta = await admin.PostAsJsonAsync("/painel/quinzenas", new DatasQuinzenaRequisicao(inicio, fim));
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resposta.Content.ReadFromJsonAsync<QuinzenaSalvaResposta>())!.Id;
    }
}
