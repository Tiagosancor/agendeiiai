using FluentAssertions;
using Plataforma.Dominio.Servicos;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class ProfissionalServicoTestes
{
    [Fact]
    public void Criar_com_preco_negativo_lanca_excecao()
    {
        var acao = () => ProfissionalServico.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), precoPersonalizado: -10m);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_com_duracao_zero_lanca_excecao()
    {
        var acao = () => ProfissionalServico.Criar(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), duracaoPersonalizadaMinutos: 0);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criar_sem_overrides_funciona()
    {
        var vinculo = ProfissionalServico.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        vinculo.PrecoPersonalizado.Should().BeNull();
        vinculo.DuracaoPersonalizadaMinutos.Should().BeNull();
    }
}
