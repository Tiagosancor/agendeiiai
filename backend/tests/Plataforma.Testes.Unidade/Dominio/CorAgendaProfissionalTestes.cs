using FluentAssertions;
using Plataforma.Dominio.Profissionais;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class CorAgendaProfissionalTestes
{
    [Fact]
    public void Profissional_novo_nao_tem_cor()
    {
        Profissional.Criar(Guid.NewGuid(), "Ana").CorAgenda.Should().BeNull();
    }

    [Theory]
    [InlineData("#1A2B3C", "#1a2b3c")]
    [InlineData("#abcdef", "#abcdef")]
    [InlineData("  #00FF7f  ", "#00ff7f")]
    public void Cor_valida_e_normalizada_em_minusculas(string informada, string esperada)
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Ana");

        profissional.DefinirCorAgenda(informada);

        profissional.CorAgenda.Should().Be(esperada);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Vazio_tira_a_cor(string? vazio)
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Ana");
        profissional.DefinirCorAgenda("#123456");

        profissional.DefinirCorAgenda(vazio);

        profissional.CorAgenda.Should().BeNull();
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("#123")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#12345678")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    [InlineData("rgb(1, 2, 3)")]
    [InlineData("linear-gradient(red, blue)")]
    [InlineData("var(--cor)")]
    [InlineData("#123456;background:url(x)")]
    [InlineData("#12 456")]
    public void Formato_invalido_e_recusado_e_nao_muda_a_cor(string invalida)
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Ana");
        profissional.DefinirCorAgenda("#123456");

        var acao = () => profissional.DefinirCorAgenda(invalida);

        acao.Should().Throw<ArgumentException>().WithMessage("*#RRGGBB*");
        profissional.CorAgenda.Should().Be("#123456");
    }

    [Fact]
    public void Desativar_reativar_e_excluir_preservam_a_cor()
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Ana");
        profissional.DefinirCorAgenda("#123456");

        profissional.Desativar();
        profissional.Ativar();
        profissional.CorAgenda.Should().Be("#123456");

        profissional.Excluir(DateTimeOffset.UtcNow);
        profissional.CorAgenda.Should().Be("#123456", "cor não é dado pessoal; a exclusão segue apagando só os dados pessoais");
    }
}
