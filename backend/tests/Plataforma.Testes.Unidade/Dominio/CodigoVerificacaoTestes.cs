using FluentAssertions;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Verificacao;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class CodigoVerificacaoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly TelefoneE164 Telefone = TelefoneE164.Criar("+5571988887777");

    [Fact]
    public void ConferirEMarcar_com_hash_certo_funciona_uma_unica_vez()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-certo", Agora);

        codigo.ConferirEMarcar("hash-certo", Agora).Should().BeTrue();
        codigo.Usado.Should().BeTrue();

        // Uso único (seção 8.1.2) — o mesmo código não vale de novo.
        codigo.ConferirEMarcar("hash-certo", Agora).Should().BeFalse();
    }

    [Fact]
    public void ConferirEMarcar_com_hash_errado_consome_uma_tentativa()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-certo", Agora);

        codigo.ConferirEMarcar("hash-errado", Agora).Should().BeFalse();

        codigo.TentativasRestantes.Should().Be(4);
        codigo.Usado.Should().BeFalse();
    }

    [Fact]
    public void Apos_5_tentativas_erradas_o_codigo_certo_tambem_falha()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-certo", Agora);

        for (var i = 0; i < 5; i++)
            codigo.ConferirEMarcar("hash-errado", Agora);

        codigo.TentativasRestantes.Should().Be(0);
        codigo.ConferirEMarcar("hash-certo", Agora).Should().BeFalse();
    }

    [Fact]
    public void Codigo_expirado_falha_mesmo_com_hash_certo()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-certo", Agora);

        var seisMinutosDepois = Agora.AddMinutes(6); // válido só por 5 min (seção 8.1.2)

        codigo.ConferirEMarcar("hash-certo", seisMinutosDepois).Should().BeFalse();
    }

    [Fact]
    public void Codigo_invalidado_falha_mesmo_com_hash_certo()
    {
        var codigo = CodigoVerificacao.Criar(Guid.NewGuid(), Telefone, "hash-certo", Agora);
        codigo.Invalidar(); // um código mais novo foi solicitado (seção 8.1.2)

        codigo.ConferirEMarcar("hash-certo", Agora).Should().BeFalse();
    }
}
