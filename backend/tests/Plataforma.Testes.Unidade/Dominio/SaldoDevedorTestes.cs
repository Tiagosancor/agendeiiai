using FluentAssertions;
using Plataforma.Dominio.Comissoes;
using Plataforma.Dominio.Estoque;

namespace Plataforma.Testes.Unidade.Dominio;

public class SaldoDevedorTestes
{
    private static readonly Guid Negocio = Guid.NewGuid();

    [Fact]
    public void Quitacao_desconta_do_mais_antigo_ate_a_comissao_acabar_e_o_resto_fica_em_aberto()
    {
        var antigo = new SaldoEmAberto(Guid.NewGuid(), TipoLancamentoSaldo.Vale, 30m);
        var meio = new SaldoEmAberto(Guid.NewGuid(), TipoLancamentoSaldo.ConsumoInterno, 25m);
        var novo = new SaldoEmAberto(Guid.NewGuid(), TipoLancamentoSaldo.Vale, 10m);

        var (descontos, liquido, restante) = CalculadoraQuitacao.Aplicar(40m, [antigo, meio, novo]);

        descontos.Should().Equal(
            new DescontoAplicado(antigo.LancamentoId, TipoLancamentoSaldo.Vale, 30m),
            new DescontoAplicado(meio.LancamentoId, TipoLancamentoSaldo.ConsumoInterno, 10m));
        liquido.Should().Be(0m);
        restante.Should().Be(25m); // 15 do consumo + 10 do vale novo
    }

    [Fact]
    public void Sem_saldo_devedor_o_liquido_e_a_comissao_e_comissao_zero_nao_desconta_nada()
    {
        CalculadoraQuitacao.Aplicar(12.5m, []).Liquido.Should().Be(12.5m);

        var (descontos, liquido, restante) = CalculadoraQuitacao.Aplicar(0m, [new SaldoEmAberto(Guid.NewGuid(), TipoLancamentoSaldo.Vale, 50m)]);
        descontos.Should().BeEmpty();
        liquido.Should().Be(0m);
        restante.Should().Be(50m);
    }

    [Fact]
    public void Consumo_baixa_o_estoque_e_vale_pelo_valor_unitario_vezes_a_quantidade()
    {
        var produto = Produto.Criar(Negocio, "Gel", null, 8m, 25m);
        produto.RegistrarEntrada(4, 8m, null, null, null, DateTimeOffset.UtcNow);

        var (consumo, movimento) = LancamentoSaldoDevedor.CriarConsumo(
            PessoaComissao.Profissional(Guid.NewGuid()), produto, 3, 7.5m, new DateOnly(2026, 9, 1), "uso", null, DateTimeOffset.UtcNow);

        consumo.Valor.Should().Be(22.5m);
        (movimento.Tipo, movimento.Quantidade, movimento.QuantidadeDepois).Should().Be((TipoMovimentoEstoque.ConsumoInterno, -3, 1));
        consumo.MovimentoEstoqueId.Should().Be(movimento.Id);

        var mais = () => LancamentoSaldoDevedor.CriarConsumo(PessoaComissao.Profissional(Guid.NewGuid()), produto, 2, 8m, new DateOnly(2026, 9, 1), null, null, DateTimeOffset.UtcNow);
        mais.Should().Throw<EstoqueInsuficienteException>().Which.Disponivel.Should().Be(1);
    }

    [Fact]
    public void Vale_exige_valor_positivo_e_fechamento_estornado_devolve_ao_liquido()
    {
        var zero = () => LancamentoSaldoDevedor.CriarVale(Negocio, PessoaComissao.Profissional(Guid.NewGuid()), 0m, new DateOnly(2026, 9, 1), null, null);
        zero.Should().Throw<ArgumentException>();

        var fechamento = FechamentoComissao.Criar(Negocio, Guid.NewGuid(), PessoaComissao.Profissional(Guid.NewGuid()), 100m, 10m, 2, null, DateTimeOffset.UtcNow);
        fechamento.RegistrarProdutosEDescontos(comissaoProdutos: 5m, vales: 12m, consumo: 3m, saldoRestante: 7m);
        fechamento.Liquido.Should().Be(0m);

        fechamento.EstornarDesconto(TipoLancamentoSaldo.Vale, 12m);
        (fechamento.TotalVales, fechamento.Liquido).Should().Be((0m, 12m));
    }
}
