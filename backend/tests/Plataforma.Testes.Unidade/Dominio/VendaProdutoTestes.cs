using FluentAssertions;
using Plataforma.Dominio.Estoque;

namespace Plataforma.Testes.Unidade.Dominio;

public class VendaProdutoTestes
{
    private static readonly Guid Negocio = Guid.NewGuid();

    private static Produto ProdutoCom(int quantidade, string nome = "Pomada")
    {
        var produto = Produto.Criar(Negocio, nome, null, 10m, 30m);
        if (quantidade > 0)
            produto.RegistrarEntrada(quantidade, 10m, null, null, null, DateTimeOffset.UtcNow);
        return produto;
    }

    private static VendaProduto Venda(decimal percentual = 10m) =>
        VendaProduto.Criar(Negocio, DateTimeOffset.UtcNow, null, null, new Vendedor(Guid.NewGuid(), null, "Ana", percentual), null);

    [Fact]
    public void Soma_os_itens_baixa_o_estoque_e_calcula_a_comissao_do_vendedor()
    {
        var venda = Venda(percentual: 12.5m);
        var pomada = ProdutoCom(5);
        var cera = ProdutoCom(2, "Cera");

        var movimento = venda.AdicionarItem(pomada, 2, 29.90m);
        venda.AdicionarItem(cera, 1, 15m);

        venda.Total.Should().Be(74.80m);
        venda.ValorComissao.Should().Be(9.35m); // 12,5% de 74,80
        (movimento.Tipo, movimento.Quantidade, movimento.QuantidadeAntes, movimento.QuantidadeDepois, movimento.ValorUnitario)
            .Should().Be((TipoMovimentoEstoque.Venda, -2, 5, 3, 29.90m));
        venda.Itens.Should().HaveCount(2);
        pomada.QuantidadeEstoque.Should().Be(3);
    }

    [Fact]
    public void Recusa_mais_que_o_estoque_informando_o_disponivel()
    {
        var produto = ProdutoCom(1);

        var vender = () => Venda().AdicionarItem(produto, 2, 30m);

        vender.Should().Throw<EstoqueInsuficienteException>().Which.Disponivel.Should().Be(1);
        produto.QuantidadeEstoque.Should().Be(1);
    }

    [Fact]
    public void Tem_exatamente_um_vendedor()
    {
        var semVendedor = () => VendaProduto.Criar(Negocio, DateTimeOffset.UtcNow, null, null, new Vendedor(null, null, "?", 0m), null);
        var doisVendedores = () => VendaProduto.Criar(
            Negocio, DateTimeOffset.UtcNow, null, null, new Vendedor(Guid.NewGuid(), Guid.NewGuid(), "?", 0m), null);

        semVendedor.Should().Throw<ArgumentException>();
        doisVendedores.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Recusa_produto_repetido_inativo_ou_valor_negativo()
    {
        var venda = Venda();
        var produto = ProdutoCom(5);
        venda.AdicionarItem(produto, 1, 30m);

        venda.Invoking(v => v.AdicionarItem(produto, 1, 30m)).Should().Throw<ArgumentException>();
        venda.Invoking(v => v.AdicionarItem(ProdutoCom(5, "Outro"), 1, -1m)).Should().Throw<ArgumentException>();

        var inativo = ProdutoCom(5, "Inativo");
        inativo.Desativar();
        venda.Invoking(v => v.AdicionarItem(inativo, 1, 30m)).Should().Throw<ArgumentException>();
        inativo.QuantidadeEstoque.Should().Be(5);
    }
}
