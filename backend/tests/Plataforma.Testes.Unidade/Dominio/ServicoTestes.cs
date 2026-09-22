using FluentAssertions;
using Plataforma.Dominio.Servicos;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class ServicoTestes
{
    [Fact]
    public void Criar_com_dados_validos_funciona()
    {
        var servico = Servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "Corte", 50m, 30, popular: true);

        servico.Nome.Should().Be("Corte");
        servico.Preco.Should().Be(50m);
        servico.DuracaoMinutos.Should().Be(30);
        servico.Popular.Should().BeTrue();
        servico.Ativo.Should().BeTrue();
    }

    [Fact]
    public void Criar_com_preco_negativo_lanca_excecao()
    {
        var acao = () => Servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "Corte", -1m, 30);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_com_duracao_zero_lanca_excecao()
    {
        var acao = () => Servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "Corte", 50m, 0);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Desativar_e_ativar_alternam_o_status()
    {
        var servico = Servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "Corte", 50m, 30);

        servico.Desativar();
        servico.Ativo.Should().BeFalse();

        servico.Ativar();
        servico.Ativo.Should().BeTrue();
    }
}
