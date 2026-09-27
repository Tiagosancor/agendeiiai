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
    [InlineData("71988887777", "+5571988887777")]          // só DDD + número: o jeito que as pessoas digitam
    [InlineData("(71) 98888-7777", "+5571988887777")]
    [InlineData("71 9 8888-7777", "+5571988887777")]
    [InlineData("7133334444", "+557133334444")]             // fixo: 10 dígitos
    [InlineData("071 98888-7777", "+5571988887777")]        // 0 de discagem interurbana
    [InlineData("5571988887777", "+5571988887777")]         // já com o 55, sem o '+'
    [InlineData("+55 (71) 98888-7777", "+5571988887777")]
    [InlineData("  +1 555 123 4567 ", "+15551234567")]      // com '+', vale o país digitado
    public void Aceita_o_jeito_brasileiro_de_escrever_e_completa_o_55(string digitado, string esperado) =>
        TelefoneE164.Criar(digitado).Valor.Should().Be(esperado);

    [Fact]
    public void O_mesmo_numero_escrito_de_jeitos_diferentes_e_o_mesmo_telefone() =>
        // É a chave do cliente no negócio (seção 7): não pode virar dois clientes.
        TelefoneE164.Criar("(71) 98888-7777").Should().Be(TelefoneE164.Criar("+5571988887777"));

    [Theory]
    [InlineData("988887777")]          // sem DDD
    [InlineData("71 98888-777a")]      // letra não é "consertada"
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
