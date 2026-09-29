using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Dominio.Estoque;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Estoque;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Estoque;

/// <summary>Produtos, entrada, ajuste, listas de alerta e o e-mail diário (seção 7, item 12 a/b/f da seção 14).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class EstoqueTestes : IAsyncLifetime
{
    private const string Produtos = "/painel/estoque/produtos";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public EstoqueTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Estoque_so_muda_por_movimento_que_grava_antes_e_depois_e_a_soma_bate_com_a_quantidade()
    {
        var (admin, _, usuarioId, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var id = await CriarAsync(admin, "Pomada", quantidadeInicial: 10);
        var produto = (await admin.GetFromJsonAsync<List<ProdutoResumo>>(Produtos))!.Single();
        produto.QuantidadeEstoque.Should().Be(10);
        produto.QuantidadeMinima.Should().Be(3); // padrão confirmado pelo dono
        produto.Situacao.Should().Be("Normal");

        var entrada = await admin.PostAsJsonAsync($"{Produtos}/{id}/entradas", new RegistrarEntrada(5, 11.50m, "Distribuidora X"));
        entrada.StatusCode.Should().Be(HttpStatusCode.OK);
        (await entrada.Content.ReadFromJsonAsync<ProdutoResumo>())!.QuantidadeEstoque.Should().Be(15);

        (await admin.PostAsJsonAsync($"{Produtos}/{id}/ajustes", new RegistrarAjuste(12, "")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync($"{Produtos}/{id}/entradas", new RegistrarEntrada(0, 10m)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync($"{Produtos}/{id}/ajustes", new RegistrarAjuste(12, "Quebrou um pote e sumiram dois")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var movimentos = await _fabrica.NoBancoAsync(db => db.MovimentosEstoque.IgnoreQueryFilters()
            .Where(m => m.ProdutoId == id).OrderBy(m => m.Data).ToListAsync());
        movimentos.Select(m => (m.Tipo, m.Quantidade, m.QuantidadeAntes, m.QuantidadeDepois)).Should().Equal(
            (TipoMovimentoEstoque.Entrada, 10, 0, 10),
            (TipoMovimentoEstoque.Entrada, 5, 10, 15),
            (TipoMovimentoEstoque.Ajuste, -3, 15, 12));
        movimentos.Sum(m => m.Quantidade).Should().Be(12);
        movimentos.Should().OnlyContain(m => m.UsuarioId == usuarioId);
        movimentos[1].Fornecedor.Should().Be("Distribuidora X");
        movimentos[1].ValorUnitario.Should().Be(11.50m);
        movimentos[2].Observacao.Should().Be("Quebrou um pote e sumiram dois");

        var atual = await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().SingleAsync(p => p.Id == id));
        atual.QuantidadeEstoque.Should().Be(12);

        var historico = await admin.GetFromJsonAsync<List<MovimentoEstoqueResumo>>($"{Produtos}/{id}/movimentos");
        historico!.Should().HaveCount(3);
        historico.Should().OnlyContain(m => m.Usuario == "Usuário Teste");

        (await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.EntidadeId == id)))
            .Should().Be(3); // criação, entrada e ajuste
    }

    [Fact]
    public async Task Cair_no_minimo_entra_em_estoque_baixo_zerar_em_esgotado_e_uma_entrada_tira_das_listas()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarAsync(admin, "Shampoo", quantidadeInicial: 5);
        var inativo = await CriarAsync(admin, "Fora de linha", quantidadeInicial: 0);
        (await admin.PostAsync($"{Produtos}/{inativo}/desativar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await AlertasAsync(admin)).Should().BeEquivalentTo(new AlertasEstoque([], []));

        await admin.PostAsJsonAsync($"{Produtos}/{id}/ajustes", new RegistrarAjuste(3, "Contagem"));
        var baixo = await AlertasAsync(admin);
        baixo.EstoqueBaixo.Should().ContainSingle(p => p.Id == id && p.QuantidadeEstoque == 3);
        baixo.Esgotados.Should().BeEmpty();

        await admin.PostAsJsonAsync($"{Produtos}/{id}/ajustes", new RegistrarAjuste(0, "Contagem"));
        var esgotado = await AlertasAsync(admin);
        esgotado.EstoqueBaixo.Should().BeEmpty();
        esgotado.Esgotados.Should().ContainSingle(p => p.Id == id); // o inativo não aparece

        await admin.PostAsJsonAsync($"{Produtos}/{id}/entradas", new RegistrarEntrada(10, 9m));
        (await AlertasAsync(admin)).Should().BeEquivalentTo(new AlertasEstoque([], []));
    }

    [Fact]
    public async Task Sem_gerenciar_estoque_nao_cadastra_nem_movimenta_e_quem_vende_so_ve_os_alertas()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarAsync(admin, "Cera", quantidadeInicial: 2);

        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista,
            permissoesExtras: Usuario.PermissoesPadrao(Perfil.Recepcionista).ToArray());
        (await recepcao.GetAsync(Produtos)).StatusCode.Should().Be(HttpStatusCode.Forbidden); // o preço de custo fica com quem gerencia
        (await recepcao.PostAsJsonAsync(Produtos, new CriarProduto("Outro", null, 1m, 2m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.PostAsJsonAsync($"{Produtos}/{id}/entradas", new RegistrarEntrada(1, 1m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.PostAsJsonAsync($"{Produtos}/{id}/ajustes", new RegistrarAjuste(5, "x"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.GetFromJsonAsync<AlertasEstoque>("/painel/estoque/alertas"))!.EstoqueBaixo.Should().ContainSingle();

        using var profissional = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional);
        (await profissional.GetAsync("/painel/estoque/alertas")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().SingleAsync(p => p.Id == id))).QuantidadeEstoque.Should().Be(2);
    }

    [Fact]
    public async Task Produto_sem_venda_pode_ser_excluido_e_estoque_negativo_e_recusado_pelo_banco()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarAsync(admin, "Errado", quantidadeInicial: 4);

        (await admin.DeleteAsync($"{Produtos}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().AnyAsync(p => p.Id == id))).Should().BeFalse();

        var outro = await CriarAsync(admin, "Certo", quantidadeInicial: 1);
        var recusado = await _fabrica.NoBancoAsync(async db =>
        {
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE produtos SET quantidade_estoque = -1 WHERE id = {outro}");
                return false;
            }
            catch (Npgsql.PostgresException)
            {
                return true;
            }
        });
        recusado.Should().BeTrue();
    }

    [Fact]
    public async Task Job_diario_manda_um_resumo_por_negocio_aos_administradores_sem_preco_de_custo()
    {
        var (admin, _, _, emailAdmin) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await CriarAsync(admin, "Gel Esgotado", quantidadeInicial: 0, custo: 12.34m);
        await CriarAsync(admin, "Oleo Acabando", quantidadeInicial: 2, custo: 56.78m);
        await CriarAsync(admin, "Tonico Sobrando", quantidadeInicial: 40, custo: 90.12m);

        var (outroAdmin, _, _, emailOutro) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await CriarAsync(outroAdmin, "Tudo Certo", quantidadeInicial: 50);

        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        using (var escopo = _fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<JobAlertaEstoque>().ExecutarAsync();

        var alerta = espia.Enviados.Should().ContainSingle(e => e.Destinatario == emailAdmin).Subject;
        alerta.Assunto.Should().StartWith("Produtos esgotados");
        alerta.CorpoHtml.Should().Contain("Gel Esgotado").And.Contain("Oleo Acabando").And.NotContain("Tonico Sobrando");
        alerta.CorpoHtml.Should().NotContain("12,34").And.NotContain("56,78").And.NotContain("12.34");
        espia.Enviados.Should().NotContain(e => e.Destinatario == emailOutro);
    }

    private static async Task<Guid> CriarAsync(HttpClient cliente, string nome, int quantidadeInicial, decimal custo = 10m)
    {
        var resposta = await cliente.PostAsJsonAsync(Produtos, new CriarProduto(nome, "Cabelo", custo, 35m, quantidadeInicial));
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<AlertasEstoque> AlertasAsync(HttpClient cliente) =>
        (await cliente.GetFromJsonAsync<AlertasEstoque>("/painel/estoque/alertas"))!;
}
