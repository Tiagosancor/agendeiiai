using FluentAssertions;
using Plataforma.Dominio.Usuarios;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class RedefinicaoSenhaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static RedefinicaoSenha Nova() => RedefinicaoSenha.Criar(Guid.NewGuid(), Guid.NewGuid(), "hash", Agora.AddMinutes(60));

    [Fact]
    public void Vale_ate_expirar()
    {
        var redefinicao = Nova();

        redefinicao.Valida(Agora.AddMinutes(59)).Should().BeTrue();
        redefinicao.Valida(Agora.AddMinutes(60)).Should().BeFalse();
    }

    [Fact]
    public void Uso_unico()
    {
        var redefinicao = Nova();
        redefinicao.MarcarUsada(Agora);

        redefinicao.Valida(Agora).Should().BeFalse();
        redefinicao.Invoking(r => r.MarcarUsada(Agora)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Invalidada_nao_pode_ser_usada()
    {
        var redefinicao = Nova();
        redefinicao.Invalidar(Agora);

        redefinicao.Valida(Agora).Should().BeFalse();
        redefinicao.Invoking(r => r.MarcarUsada(Agora)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Exige_hash() =>
        FluentActions.Invoking(() => RedefinicaoSenha.Criar(Guid.NewGuid(), Guid.NewGuid(), " ", Agora))
            .Should().Throw<ArgumentException>();
}
