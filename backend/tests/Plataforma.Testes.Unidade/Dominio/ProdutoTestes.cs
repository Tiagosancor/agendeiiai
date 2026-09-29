using FluentAssertions;
using Plataforma.Dominio.Estoque;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class ProdutoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Saida_maior_que_o_estoque_e_recusada_mostrando_a_quantidade_disponivel()
    {
        var produto = Produto.Criar(Guid.NewGuid(), "Pomada", null, 10m, 30m);
        produto.RegistrarEntrada(2, 10m, null, null, null, Agora);

        var acao = () => produto.RegistrarSaida(TipoMovimentoEstoque.Venda, 3, 30m, null, null, Agora);

        acao.Should().Throw<EstoqueInsuficienteException>().Which.Disponivel.Should().Be(2);
        produto.QuantidadeEstoque.Should().Be(2);
    }

    [Fact]
    public void Saida_desconta_e_grava_antes_e_depois()
    {
        var produto = Produto.Criar(Guid.NewGuid(), "Pomada", null, 10m, 30m);
        produto.RegistrarEntrada(5, 10m, null, null, null, Agora);

        var movimento = produto.RegistrarSaida(TipoMovimentoEstoque.ConsumoInterno, 2, 10m, null, null, Agora);

        produto.QuantidadeEstoque.Should().Be(3);
        (movimento.Quantidade, movimento.QuantidadeAntes, movimento.QuantidadeDepois).Should().Be((-2, 5, 3));
        produto.EstoqueBaixo.Should().BeTrue();
    }

    [Fact]
    public void Situacao_segue_o_minimo_e_o_zero()
    {
        var produto = Produto.Criar(Guid.NewGuid(), "Pomada", null, 10m, 30m, quantidadeMinima: 2);
        produto.Esgotado.Should().BeTrue();

        produto.RegistrarEntrada(3, 10m, null, null, null, Agora);
        (produto.EstoqueBaixo, produto.Esgotado).Should().Be((false, false));

        produto.RegistrarAjuste(2, "Contagem", null, Agora);
        produto.EstoqueBaixo.Should().BeTrue();

        produto.Desativar();
        (produto.EstoqueBaixo, produto.Esgotado).Should().Be((false, false));
    }

    [Fact]
    public void Ajuste_exige_motivo_e_mudanca_e_entrada_exige_quantidade_positiva()
    {
        var produto = Produto.Criar(Guid.NewGuid(), "Pomada", null, 10m, 30m);
        produto.RegistrarEntrada(4, 10m, null, null, null, Agora);

        FluentActions.Invoking(() => produto.RegistrarAjuste(3, " ", null, Agora)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => produto.RegistrarAjuste(4, "Contagem", null, Agora)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => produto.RegistrarEntrada(0, 10m, null, null, null, Agora)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Produto.Criar(Guid.NewGuid(), "X", null, -1m, 1m)).Should().Throw<ArgumentException>();
        produto.QuantidadeEstoque.Should().Be(4);
    }
}
