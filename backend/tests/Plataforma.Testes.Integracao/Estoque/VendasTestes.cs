using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Dominio.Estoque;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Estoque;

/// <summary>Venda de produto, faturamento separado e comissão de produto (seção 7, item 12 c da seção 14).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class VendasTestes : IAsyncLifetime
{
    private const string Produtos = "/painel/estoque/produtos";
    private const string Vendas = "/painel/vendas";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public VendasTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private static string Hoje => SemeadorDeComissoes.Dia(0).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Venda_avulsa_baixa_o_estoque_soma_ao_faturamento_separado_e_a_comissao_vai_so_para_quem_foi_escolhido()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var produtoId = await CriarProdutoAsync(admin, "Pomada", 40m, quantidade: 5);
        var profissionalId = await _fabrica.CriarProfissionalAsync(negocioId, "Beto");
        (await admin.PutAsJsonAsync($"/painel/profissionais/{profissionalId}/comissao", new ConfiguracaoComissao(10m, false, 10m)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Recepcionista pura (sem cadastro de profissional): percentual de produto no próprio usuário.
        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista,
            permissoesExtras: Usuario.PermissoesPadrao(Perfil.Recepcionista).ToArray());
        var recepcaoId = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters()
            .Where(u => u.NegocioId == negocioId && u.Perfil == Perfil.Recepcionista).Select(u => u.Id).SingleAsync());
        (await admin.PutAsJsonAsync($"/painel/usuarios/{recepcaoId}/comissao-produto", new DefinirPercentualProduto(5m)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var opcoes = await admin.GetFromJsonAsync<OpcoesVenda>($"{Vendas}/opcoes");
        opcoes!.Vendedores.Should().Contain(v => v.ProfissionalId == profissionalId).And.Contain(v => v.UsuarioId == recepcaoId);
        opcoes.Produtos.Should().ContainSingle(p => p.Id == produtoId && p.PrecoVenda == 40m && p.QuantidadeEstoque == 5);

        // O administrador lança, mas quem vendeu foi a recepção, com desconto pontual no preço.
        var venda = await admin.PostAsJsonAsync(Vendas, new LancarVenda(
            [new ItemLancarVenda(produtoId, 2, 35m)], null, recepcaoId, NovoCliente: new NovoClienteVenda("Carla Dias", "71 98888-7777")));
        venda.StatusCode.Should().Be(HttpStatusCode.Created);
        var lancada = (await venda.Content.ReadFromJsonAsync<VendaLancada>())!;
        lancada.Total.Should().Be(70m);

        var gravada = await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().Include(v => v.Itens).SingleAsync());
        gravada.VendedorUsuarioId.Should().Be(recepcaoId);
        gravada.VendedorProfissionalId.Should().BeNull();
        gravada.PercentualComissao.Should().Be(5m);
        gravada.ValorComissao.Should().Be(3.50m);
        gravada.ClienteId.Should().NotBeNull();

        var movimento = await _fabrica.NoBancoAsync(db => db.MovimentosEstoque.IgnoreQueryFilters()
            .SingleAsync(m => m.ProdutoId == produtoId && m.Tipo == TipoMovimentoEstoque.Venda));
        (movimento.Quantidade, movimento.QuantidadeAntes, movimento.QuantidadeDepois, movimento.ValorUnitario)
            .Should().Be((-2, 5, 3, 35m));
        gravada.Itens.Single().MovimentoEstoqueId.Should().Be(movimento.Id);

        // Faturamento do dia e do mês: produtos separados dos serviços.
        foreach (var inicio in new[] { Hoje, SemeadorDeComissoes.Dia(0).AddDays(1 - SemeadorDeComissoes.Dia(0).Day).ToString("yyyy-MM-dd") })
        {
            var resumo = (await admin.GetFromJsonAsync<ResumoFinanceiro>($"/painel/financeiro/resumo?inicio={inicio}&fim={Hoje}"))!;
            resumo.Total.Should().Be(70m);
            resumo.TotalProdutos.Should().Be(70m);
            resumo.TotalServicos.Should().Be(0m);
            resumo.QuantidadeVendas.Should().Be(1);
            resumo.PorProduto.Should().ContainSingle(p => p.ProdutoId == produtoId && p.Quantidade == 2 && p.Total == 70m);
        }

        // Só a recepção recebe: "Minhas comissões" dela mostra a de produto; o profissional não ganha nada.
        var periodo = $"de={Hoje}&ate={Hoje}";
        var minhasRecepcao = (await recepcao.GetFromJsonAsync<ComissoesDoProfissional>($"/painel/comissoes/minhas?{periodo}"))!;
        minhasRecepcao.ProfissionalId.Should().BeNull();
        minhasRecepcao.Totais.TotalComissao.Should().Be(0m);
        minhasRecepcao.Produtos.Totais.Should().Be(new TotaisComissaoProduto(3.50m, 70m, 1));
        minhasRecepcao.Produtos.PercentualAtual.Should().Be(5m);
        minhasRecepcao.Produtos.Itens.Should().ContainSingle(i => i.Cliente == "Carla" && i.Produtos == "Pomada × 2");
        minhasRecepcao.TotalGeral.Should().Be(3.50m);

        var equipe = await admin.GetFromJsonAsync<List<ResumoComissaoProfissional>>($"/painel/comissoes/resumo?{periodo}");
        equipe!.Single(p => p.ProfissionalId == profissionalId).Produtos.Should().Be(TotaisComissaoProduto.Zero);
        var vendedores = await admin.GetFromJsonAsync<List<ResumoComissaoVendedor>>($"/painel/comissoes/resumo/vendedores?{periodo}");
        vendedores!.Should().ContainSingle(v => v.UsuarioId == recepcaoId && v.Totais.TotalComissao == 3.50m);

        // Mudar o percentual depois não mexe na venda feita.
        await admin.PutAsJsonAsync($"/painel/usuarios/{recepcaoId}/comissao-produto", new DefinirPercentualProduto(20m));
        (await recepcao.GetFromJsonAsync<ComissoesDoProfissional>($"/painel/comissoes/minhas?{periodo}"))!
            .Produtos.Totais.TotalComissao.Should().Be(3.50m);

        // Com venda, o produto não pode mais ser excluído (desativar é o caminho).
        (await admin.DeleteAsync($"{Produtos}/{produtoId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.Acao == "VendaProduto")))
            .Should().Be(1);
        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.Acao == "AlterarComissaoProduto")))
            .Should().Be(2);
    }

    [Fact]
    public async Task Venda_maior_que_o_estoque_e_recusada_mostrando_a_quantidade_real_e_nenhum_item_baixa()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var farto = await CriarProdutoAsync(admin, "Shampoo", 30m, quantidade: 5);
        var pouco = await CriarProdutoAsync(admin, "Cera", 25m, quantidade: 1);
        var vendedor = await _fabrica.CriarProfissionalAsync(negocioId, "Ana");

        var resposta = await admin.PostAsJsonAsync(Vendas, new LancarVenda(
            [new ItemLancarVenda(farto, 2, 30m), new ItemLancarVenda(pouco, 3, 25m)], vendedor, null));

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("disponivel").GetInt32().Should().Be(1);
        corpo.GetProperty("title").GetString().Should().Contain("Cera").And.Contain("1");

        var quantidades = await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters()
            .ToDictionaryAsync(p => p.Id, p => p.QuantidadeEstoque));
        quantidades[farto].Should().Be(5);
        quantidades[pouco].Should().Be(1);
        (await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().AnyAsync())).Should().BeFalse();
    }

    [Fact]
    public async Task Sem_vender_produtos_recebe_403_e_o_vendedor_precisa_ser_uma_pessoa_habilitada()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var produtoId = await CriarProdutoAsync(admin, "Óleo", 20m, quantidade: 3);
        var profissionalId = await _fabrica.CriarProfissionalAsync(negocioId, "Caio");

        using var semPermissao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional);
        (await semPermissao.GetAsync($"{Vendas}/opcoes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await semPermissao.PostAsJsonAsync(Vendas, new LancarVenda([new ItemLancarVenda(produtoId, 1, 20m)], profissionalId, null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Usuário sem "vender produtos" não pode ser escolhido; duas pessoas na mesma venda também não.
        var semPermissaoId = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters()
            .Where(u => u.NegocioId == negocioId && u.Perfil == Perfil.Profissional).Select(u => u.Id).SingleAsync());
        (await admin.PostAsJsonAsync(Vendas, new LancarVenda([new ItemLancarVenda(produtoId, 1, 20m)], null, semPermissaoId)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(Vendas, new LancarVenda([new ItemLancarVenda(produtoId, 1, 20m)], profissionalId, semPermissaoId)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync(Vendas, new LancarVenda([], profissionalId, null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().SingleAsync(p => p.Id == produtoId))).QuantidadeEstoque.Should().Be(3);
    }

    [Fact]
    public async Task Duas_vendas_simultaneas_no_limite_do_estoque_nao_deixam_a_quantidade_negativa()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var produtoId = await CriarProdutoAsync(admin, "Último pote", 50m, quantidade: 2);
        var vendedor = await _fabrica.CriarProfissionalAsync(negocioId, "Dani");

        var respostas = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            admin.PostAsJsonAsync(Vendas, new LancarVenda([new ItemLancarVenda(produtoId, 1, 50m)], vendedor, null))));

        respostas.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(2);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(8);
        (await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().SingleAsync(p => p.Id == produtoId))).QuantidadeEstoque.Should().Be(0);
        (await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().CountAsync())).Should().Be(2);
        var movimentos = await _fabrica.NoBancoAsync(db => db.MovimentosEstoque.IgnoreQueryFilters()
            .Where(m => m.ProdutoId == produtoId).ToListAsync());
        movimentos.Sum(m => m.Quantidade).Should().Be(0);
    }

    [Fact]
    public async Task Venda_no_atendimento_gera_as_duas_comissoes_separadas_para_o_profissional()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var produtoId = await CriarProdutoAsync(admin, "Balm", 40m, quantidade: 4);
        var profissionalId = await _fabrica.CriarProfissionalAsync(negocioId, "Edu");
        await admin.PutAsJsonAsync($"/painel/profissionais/{profissionalId}/comissao", new ConfiguracaoComissao(10m, false, 15m));
        using var profissional = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, profissionalId);

        var inicio = DateTimeOffset.UtcNow.AddMinutes(-40);
        var agendamentoId = await _fabrica.SemearEConcluirAsync(admin, negocioId, profissionalId, inicio);
        (await admin.PostAsJsonAsync("/painel/pagamentos", new RegistrarPagamento(agendamentoId, 50m, "Pix")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // A sugestão de "Vendido por" é o profissional do atendimento.
        var opcoes = await admin.GetFromJsonAsync<OpcoesVenda>($"{Vendas}/opcoes?agendamentoId={agendamentoId}");
        opcoes!.VendedorSugerido!.ProfissionalId.Should().Be(profissionalId);

        (await admin.PostAsJsonAsync(Vendas, new LancarVenda([new ItemLancarVenda(produtoId, 1, 40m)], profissionalId, null, agendamentoId)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var venda = await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().SingleAsync());
        var clienteDoAtendimento = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters()
            .Where(a => a.Id == agendamentoId).Select(a => a.ClienteId).SingleAsync());
        venda.AgendamentoId.Should().Be(agendamentoId);
        venda.ClienteId.Should().Be(clienteDoAtendimento);

        var dia = SemeadorDeComissoes.Dia(0);
        var diaDoAtendimento = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(inicio, SemeadorDeComissoes.Fuso).DateTime);
        var periodo = $"de={(diaDoAtendimento < dia ? diaDoAtendimento : dia):yyyy-MM-dd}&ate={dia:yyyy-MM-dd}";
        var minhas = (await profissional.GetFromJsonAsync<ComissoesDoProfissional>($"/painel/comissoes/minhas?{periodo}"))!;
        minhas.Totais.TotalComissao.Should().Be(5m);                    // 10% de R$ 50 (serviço)
        minhas.Produtos.Totais.TotalComissao.Should().Be(6m);           // 15% de R$ 40 (produto), separado
        minhas.TotalGeral.Should().Be(11m);

        var resumo = (await admin.GetFromJsonAsync<ResumoFinanceiro>(
            $"/painel/financeiro/resumo?inicio={(diaDoAtendimento < dia ? diaDoAtendimento : dia):yyyy-MM-dd}&fim={dia:yyyy-MM-dd}"))!;
        (resumo.TotalServicos, resumo.TotalProdutos, resumo.Total).Should().Be((50m, 40m, 90m));

        var lista = await admin.GetFromJsonAsync<List<VendaResumo>>($"{Vendas}?de={dia:yyyy-MM-dd}&ate={dia:yyyy-MM-dd}");
        lista!.Should().ContainSingle(v => v.AgendamentoId == agendamentoId && v.Vendedor == "Edu" && v.Cliente == "Cliente Sobrenome");
    }

    [Fact]
    public async Task Estorno_devolve_ao_estoque_tira_do_faturamento_e_da_comissao_e_so_quem_gerencia_estoque_estorna()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var produtoId = await CriarProdutoAsync(admin, "Pomada", 40m, quantidade: 5);
        var profissionalId = await _fabrica.CriarProfissionalAsync(negocioId, "Fábio");
        await admin.PutAsJsonAsync($"/painel/profissionais/{profissionalId}/comissao", new ConfiguracaoComissao(0m, false, 10m));
        using var profissional = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, profissionalId);

        var lancada = await (await admin.PostAsJsonAsync(Vendas, new LancarVenda([new ItemLancarVenda(produtoId, 2, 40m)], profissionalId, null)))
            .Content.ReadFromJsonAsync<VendaLancada>();
        var vendaId = lancada!.VendaId;

        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista,
            permissoesExtras: Usuario.PermissoesPadrao(Perfil.Recepcionista).ToArray());
        (await recepcao.PostAsJsonAsync($"{Vendas}/{vendaId}/estornar", new EstornarVenda("Engano"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync($"{Vendas}/{vendaId}/estornar", new EstornarVenda(" "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await admin.PostAsJsonAsync($"{Vendas}/{vendaId}/estornar", new EstornarVenda("Cliente devolveu")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync($"{Vendas}/{vendaId}/estornar", new EstornarVenda("De novo"))).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().SingleAsync(p => p.Id == produtoId))).QuantidadeEstoque.Should().Be(5);
        var movimentos = await _fabrica.NoBancoAsync(db => db.MovimentosEstoque.IgnoreQueryFilters().Where(m => m.ProdutoId == produtoId).ToListAsync());
        movimentos.Should().ContainSingle(m => m.Observacao == "Estorno de venda" && m.Quantidade == 2);
        movimentos.Sum(m => m.Quantidade).Should().Be(5);

        var resumo = (await admin.GetFromJsonAsync<ResumoFinanceiro>($"/painel/financeiro/resumo?inicio={Hoje}&fim={Hoje}"))!;
        (resumo.TotalProdutos, resumo.QuantidadeVendas).Should().Be((0m, 0));
        (await profissional.GetFromJsonAsync<ComissoesDoProfissional>($"/painel/comissoes/minhas?de={Hoje}&ate={Hoje}"))!
            .Produtos.Totais.Should().Be(TotaisComissaoProduto.Zero);

        var lista = await admin.GetFromJsonAsync<List<VendaResumo>>($"{Vendas}?de={Hoje}&ate={Hoje}");
        lista!.Should().ContainSingle(v => v.Id == vendaId && v.Estornada && v.MotivoEstorno == "Cliente devolveu");
        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.Acao == "EstornarVenda"))).Should().Be(1);
    }

    [Fact]
    public async Task Venda_cuja_comissao_entrou_em_quinzena_fechada_nao_e_estornada()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var produtoId = await CriarProdutoAsync(admin, "Cera", 30m, quantidade: 3);
        var profissionalId = await _fabrica.CriarProfissionalAsync(negocioId, "Gil");
        await admin.PutAsJsonAsync($"/painel/profissionais/{profissionalId}/comissao", new ConfiguracaoComissao(10m, true, 10m));

        var vendaId = (await (await admin.PostAsJsonAsync(Vendas, new LancarVenda([new ItemLancarVenda(produtoId, 1, 30m)], profissionalId, null)))
            .Content.ReadFromJsonAsync<VendaLancada>())!.VendaId;

        var quinzena = (await (await admin.PostAsJsonAsync("/painel/quinzenas",
                new Plataforma.Api.Controllers.Painel.DatasQuinzenaRequisicao(SemeadorDeComissoes.Dia(-1), SemeadorDeComissoes.Dia(1))))
            .Content.ReadFromJsonAsync<Plataforma.Api.Controllers.Painel.QuinzenaSalvaResposta>())!.Id;
        (await admin.PostAsJsonAsync($"/painel/quinzenas/{quinzena}/fechar", new Plataforma.Api.Controllers.Painel.FecharQuinzenaRequisicao(false)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var estorno = await admin.PostAsJsonAsync($"{Vendas}/{vendaId}/estornar", new EstornarVenda("Engano"));
        estorno.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await estorno.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("quinzena_fechada");
        (await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().SingleAsync(p => p.Id == produtoId))).QuantidadeEstoque.Should().Be(2);
    }

    private static async Task<Guid> CriarProdutoAsync(HttpClient admin, string nome, decimal precoVenda, int quantidade)
    {
        var resposta = await admin.PostAsJsonAsync(Produtos, new CriarProduto(nome, null, precoVenda / 2, precoVenda, quantidade));
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }
}
