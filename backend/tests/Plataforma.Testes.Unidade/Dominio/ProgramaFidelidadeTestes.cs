using FluentAssertions;
using Plataforma.Dominio.Fidelidade;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class ProgramaFidelidadeTestes
{
    [Fact]
    public void Criar_com_dados_validos_fica_ativo()
    {
        var programa = ProgramaFidelidade.Criar(Guid.NewGuid(), 10, "Corte grátis");

        programa.SelosNecessarios.Should().Be(10);
        programa.DescricaoRecompensa.Should().Be("Corte grátis");
        programa.Ativo.Should().BeTrue();
    }

    [Fact]
    public void Criar_com_selos_necessarios_zero_ou_negativo_lanca_excecao()
    {
        var acao = () => ProgramaFidelidade.Criar(Guid.NewGuid(), 0, "Corte grátis");
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_sem_descricao_lanca_excecao()
    {
        var acao = () => ProgramaFidelidade.Criar(Guid.NewGuid(), 10, "  ");
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AtualizarCondicoes_muda_selos_e_descricao()
    {
        var programa = ProgramaFidelidade.Criar(Guid.NewGuid(), 10, "Corte grátis");

        programa.AtualizarCondicoes(5, "Barba grátis");

        programa.SelosNecessarios.Should().Be(5);
        programa.DescricaoRecompensa.Should().Be("Barba grátis");
    }

    [Fact]
    public void Desativar_e_Ativar_alternam_o_estado()
    {
        var programa = ProgramaFidelidade.Criar(Guid.NewGuid(), 10, "Corte grátis");

        programa.Desativar();
        programa.Ativo.Should().BeFalse();

        programa.Ativar();
        programa.Ativo.Should().BeTrue();
    }
}
