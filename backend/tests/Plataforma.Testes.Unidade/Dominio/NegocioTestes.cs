using FluentAssertions;
using Plataforma.Dominio.Negocios;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class NegocioTestes
{
    [Fact]
    public void Criar_preenche_os_campos_e_comeca_ativo()
    {
        var slug = Slug.Criar("acme");

        var negocio = Negocio.Criar(slug, "Acme Barbearia", TipoNegocio.Barbearia);

        negocio.Slug.Should().Be(slug);
        negocio.NomeExibido.Should().Be("Acme Barbearia");
        negocio.Tipo.Should().Be(TipoNegocio.Barbearia);
        negocio.Fuso.Should().Be("America/Sao_Paulo");
        negocio.Ativo.Should().BeTrue();
    }

    [Fact]
    public void Criar_sem_nome_exibido_lanca_excecao()
    {
        var acao = () => Negocio.Criar(Slug.Criar("acme"), "  ", TipoNegocio.Salao);

        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Desativar_e_ativar_alternam_o_status()
    {
        var negocio = Negocio.Criar(Slug.Criar("acme"), "Acme", TipoNegocio.Autonomo);

        negocio.Desativar();
        negocio.Ativo.Should().BeFalse();

        negocio.Ativar();
        negocio.Ativo.Should().BeTrue();
    }

    [Theory]
    [InlineData("#1A2B3C", "#1a2b3c")]
    [InlineData(" #ffffff ", "#ffffff")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Cor_de_fundo_aceita_so_hexadecimal_de_6_digitos(string? cor, string? esperado)
    {
        var negocio = Negocio.Criar(Slug.Criar("acme"), "Acme", TipoNegocio.Salao);

        negocio.DefinirCorFundo(cor);

        negocio.CorFundo.Should().Be(esperado);
    }

    [Theory]
    [InlineData("azul")]
    [InlineData("#fff")]
    [InlineData("#12345g")]
    [InlineData("red;background:url(x)")]
    public void Cor_de_fundo_invalida_lanca_e_mantem_a_anterior(string cor)
    {
        var negocio = Negocio.Criar(Slug.Criar("acme"), "Acme", TipoNegocio.Salao);
        negocio.DefinirCorFundo("#000000");

        FluentActions.Invoking(() => negocio.DefinirCorFundo(cor)).Should().Throw<ArgumentException>();
        negocio.CorFundo.Should().Be("#000000");
    }

    [Fact]
    public void Trocar_a_imagem_de_fundo_devolve_a_chave_anterior()
    {
        var negocio = Negocio.Criar(Slug.Criar("acme"), "Acme", TipoNegocio.Salao);

        negocio.DefinirImagemFundo("a").Should().BeNull();
        negocio.DefinirImagemFundo("b").Should().Be("a");
        negocio.DefinirImagemFundo(null).Should().Be("b");
        negocio.ImagemFundo.Should().BeNull();
    }
}
