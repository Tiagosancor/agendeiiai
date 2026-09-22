using FluentAssertions;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class HorarioTrabalhoTestes
{
    [Fact]
    public void Criar_com_fim_depois_do_inicio_funciona()
    {
        var horario = HorarioTrabalho.Criar(
            Guid.NewGuid(), Guid.NewGuid(), DiaSemana.Segunda, new TimeOnly(9, 0), new TimeOnly(12, 0));

        horario.Inicio.Should().Be(new TimeOnly(9, 0));
        horario.Fim.Should().Be(new TimeOnly(12, 0));
    }

    [Fact]
    public void Criar_com_fim_antes_ou_igual_ao_inicio_lanca_excecao()
    {
        var acao = () => HorarioTrabalho.Criar(
            Guid.NewGuid(), Guid.NewGuid(), DiaSemana.Segunda, new TimeOnly(12, 0), new TimeOnly(12, 0));

        acao.Should().Throw<ArgumentException>();
    }
}
