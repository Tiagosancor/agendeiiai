using FluentAssertions;
using Plataforma.Dominio.Profissionais;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class BloqueioAgendaTestes
{
    [Fact]
    public void Criar_com_fim_depois_do_inicio_funciona()
    {
        var inicio = DateTimeOffset.UtcNow;
        var bloqueio = BloqueioAgenda.Criar(Guid.NewGuid(), Guid.NewGuid(), inicio, inicio.AddHours(2), "Folga");

        bloqueio.Motivo.Should().Be("Folga");
    }

    [Fact]
    public void Criar_com_fim_antes_do_inicio_lanca_excecao()
    {
        var inicio = DateTimeOffset.UtcNow;
        var acao = () => BloqueioAgenda.Criar(Guid.NewGuid(), Guid.NewGuid(), inicio, inicio.AddHours(-1));

        acao.Should().Throw<ArgumentException>();
    }
}
