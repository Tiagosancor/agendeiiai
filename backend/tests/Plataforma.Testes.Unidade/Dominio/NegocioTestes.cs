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
}
