using FluentAssertions;
using Plataforma.Dominio.Comum;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class TelefoneE164Testes
{
    [Theory]
    [InlineData("+5571988887777")]
    [InlineData("+15551234567")]
    public void Aceita_formato_e164_valido(string valor)
    {
        var telefone = TelefoneE164.Criar(valor);
        telefone.Valor.Should().Be(valor);
    }

    [Theory]
    [InlineData("5571988887777")] // sem o '+'
    [InlineData("+0571988887777")] // começa com 0 depois do '+'
    [InlineData("+55")] // curto demais
    [InlineData("")]
    public void Rejeita_formato_invalido(string valor)
    {
        var acao = () => TelefoneE164.Criar(valor);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Mascarado_esconde_o_meio_do_numero()
    {
        var telefone = TelefoneE164.Criar("+5571988887777");
        var mascarado = telefone.Mascarado();

        mascarado.Should().StartWith("+5571");
        mascarado.Should().EndWith("7777");
        mascarado.Should().Contain("****");
        mascarado.Should().NotContain("88888");
    }
}
