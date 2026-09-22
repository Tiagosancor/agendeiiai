using FluentAssertions;
using Plataforma.Infraestrutura.MultiTenant;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

public sealed class ContextoNegocioTestes
{
    [Fact]
    public void Comeca_sem_negocio_definido()
    {
        var contexto = new ContextoNegocio();

        contexto.TemNegocio.Should().BeFalse();
        contexto.NegocioId.Should().BeNull();
    }

    [Fact]
    public void Definir_marca_o_negocio_atual()
    {
        var negocioId = Guid.NewGuid();
        var contexto = new ContextoNegocio();

        contexto.Definir(negocioId);

        contexto.TemNegocio.Should().BeTrue();
        contexto.NegocioId.Should().Be(negocioId);
    }

    [Fact]
    public void Definir_o_mesmo_negocio_duas_vezes_nao_lanca_excecao()
    {
        var negocioId = Guid.NewGuid();
        var contexto = new ContextoNegocio();
        contexto.Definir(negocioId);

        var acao = () => contexto.Definir(negocioId);

        acao.Should().NotThrow();
    }

    [Fact]
    public void Trocar_o_negocio_ja_definido_lanca_excecao()
    {
        var contexto = new ContextoNegocio();
        contexto.Definir(Guid.NewGuid());

        var acao = () => contexto.Definir(Guid.NewGuid());

        acao.Should().Throw<InvalidOperationException>();
    }
}
