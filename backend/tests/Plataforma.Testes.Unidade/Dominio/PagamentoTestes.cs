using FluentAssertions;
using Plataforma.Dominio.Financeiro;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class PagamentoTestes
{
    [Fact]
    public void Criar_com_valor_valido_funciona()
    {
        var pagamento = Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), 50m, FormaPagamento.Pix);

        pagamento.Valor.Should().Be(50m);
        pagamento.Forma.Should().Be(FormaPagamento.Pix);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Criar_com_valor_zero_ou_negativo_lanca_excecao(decimal valor)
    {
        var acao = () => Pagamento.Criar(Guid.NewGuid(), Guid.NewGuid(), valor, FormaPagamento.Dinheiro);

        acao.Should().Throw<ArgumentException>();
    }
}
