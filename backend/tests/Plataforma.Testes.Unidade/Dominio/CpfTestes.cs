using FluentAssertions;
using Plataforma.Dominio.Comum;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class CpfTestes
{
    [Theory]
    [InlineData("111.444.777-35")]
    [InlineData("11144477735")]
    [InlineData(" 111 444 777 35 ")]
    public void Aceita_cpf_valido_em_varios_formatos(string valor)
    {
        var cpf = Cpf.Criar(valor);
        cpf.Digitos.Should().Be("11144477735");
    }

    [Theory]
    [InlineData("11111111111")] // todos os dígitos iguais
    [InlineData("00000000000")]
    [InlineData("111.444.777-36")] // dígito verificador errado
    [InlineData("123")] // curto demais
    [InlineData("")]
    public void Rejeita_cpf_invalido(string valor)
    {
        var acao = () => Cpf.Criar(valor);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Mascarado_esconde_primeiro_grupo_e_digitos_verificadores()
    {
        var cpf = Cpf.Criar("111.444.777-35");
        cpf.Mascarado().Should().Be("***.444.777-**");
    }

    [Fact]
    public void ComPontuacao_devolve_formato_tradicional()
    {
        var cpf = Cpf.Criar("11144477735");
        cpf.ComPontuacao().Should().Be("111.444.777-35");
    }

    [Fact]
    public void TentarCriar_nao_lanca_excecao_para_valor_invalido()
    {
        var conseguiu = Cpf.TentarCriar("11111111111", out var cpf);

        conseguiu.Should().BeFalse();
        cpf.Should().BeNull();
    }
}
