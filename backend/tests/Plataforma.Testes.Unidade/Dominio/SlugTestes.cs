using FluentAssertions;
using Plataforma.Dominio.Negocios;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class SlugTestes
{
    [Theory]
    [InlineData("acme")]
    [InlineData("tiago-barber")]
    [InlineData("abc")]
    [InlineData("a1b2c3")]
    public void Aceita_formatos_validos(string valor)
    {
        var slug = Slug.Criar(valor);
        slug.Valor.Should().Be(valor);
    }

    [Fact]
    public void Normaliza_para_minusculas()
    {
        Slug.Criar("ACME").Valor.Should().Be("acme");
    }

    [Theory]
    [InlineData("ab")] // menor que o mínimo
    [InlineData("-acme")] // começa com hífen
    [InlineData("acme-")] // termina com hífen
    [InlineData("acme_barber")] // caractere não permitido
    [InlineData("acme barber")] // espaço
    [InlineData("")]
    public void Rejeita_formatos_invalidos(string valor)
    {
        var acao = () => Slug.Criar(valor);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejeita_valor_maior_que_o_tamanho_maximo()
    {
        var valorGrande = new string('a', Slug.TamanhoMaximo + 1);
        var acao = () => Slug.Criar(valorGrande);
        acao.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("www")]
    [InlineData("app")]
    [InlineData("api")]
    [InlineData("admin")]
    public void Rejeita_slugs_reservados(string valor)
    {
        var acao = () => Slug.Criar(valor);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TentarCriar_nao_lanca_excecao_para_valor_invalido()
    {
        var conseguiu = Slug.TentarCriar("app", out var slug);

        conseguiu.Should().BeFalse();
        slug.Should().BeNull();
    }

    [Fact]
    public void TentarCriar_devolve_slug_para_valor_valido()
    {
        var conseguiu = Slug.TentarCriar("acme", out var slug);

        conseguiu.Should().BeTrue();
        slug!.Valor.Should().Be("acme");
    }
}
