using FluentAssertions;
using Plataforma.Dominio.WhatsAppAgente;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class NomeClienteWhatsAppTestes
{
    [Theory]
    [InlineData("Maria", "Maria")]
    [InlineData("  Maria   da   Silva ", "Maria da Silva")]
    [InlineData("José Antônio Çelso", "José Antônio Çelso")]
    [InlineData("D'Ávila", "D'Ávila")]
    [InlineData("Ana-Clara", "Ana-Clara")]
    [InlineData("Dr. João", "Dr. João")]
    [InlineData("J. Silva", "J. Silva")]
    [InlineData("Li", "Li")]
    public void Nomes_validos_sao_aceitos_e_normalizados(string bruto, string esperado)
    {
        NomeClienteWhatsApp.TentarNormalizar(bruto, out var nome).Should().BeTrue();
        nome.Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("Maria 2")]
    [InlineData("R2D2")]
    [InlineData("<b>Maria</b>")]
    [InlineData("Maria<script>")]
    [InlineData("Maria > João")]
    [InlineData("maria@exemplo.com")]
    [InlineData("www.golpe.com")]
    [InlineData("golpe.com")]
    [InlineData("Maria http golpe")]
    [InlineData("https://golpe.com/pix")]
    [InlineData("Maria:Pix")]
    [InlineData("Maria/Pix")]
    [InlineData("...")]
    [InlineData("- -")]
    [InlineData("Maria\u0000")]
    public void Nomes_invalidos_sao_recusados(string? bruto) =>
        NomeClienteWhatsApp.TentarNormalizar(bruto, out _).Should().BeFalse();

    [Fact]
    public void Tamanho_maximo_e_80_depois_de_normalizar()
    {
        NomeClienteWhatsApp.EhValido(new string('a', 80)).Should().BeTrue();
        NomeClienteWhatsApp.EhValido(new string('a', 81)).Should().BeFalse();
        NomeClienteWhatsApp.EhValido("  " + new string('a', 80) + "  ").Should().BeTrue("os espaços das pontas não contam");
    }
}
