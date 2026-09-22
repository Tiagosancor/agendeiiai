using FluentAssertions;
using Plataforma.Infraestrutura.Seguranca;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

public sealed class SenhaHasherTestes
{
    private readonly SenhaHasher _hasher = new();

    [Fact]
    public void Hash_nao_devolve_a_senha_em_texto_puro()
    {
        var hash = _hasher.Hash("minhaSenha123!");
        hash.Should().NotBe("minhaSenha123!");
        hash.Should().NotContain("minhaSenha123!");
    }

    [Fact]
    public void Verificar_aceita_a_senha_correta()
    {
        var hash = _hasher.Hash("minhaSenha123!");
        _hasher.Verificar("minhaSenha123!", hash).Should().BeTrue();
    }

    [Fact]
    public void Verificar_rejeita_senha_errada()
    {
        var hash = _hasher.Hash("minhaSenha123!");
        _hasher.Verificar("outraSenha", hash).Should().BeFalse();
    }

    [Fact]
    public void Duas_chamadas_com_a_mesma_senha_geram_hashes_diferentes()
    {
        // bcrypt usa salt aleatório — mesmo hash duas vezes seria sinal de salt fixo (inseguro).
        var hash1 = _hasher.Hash("minhaSenha123!");
        var hash2 = _hasher.Hash("minhaSenha123!");

        hash1.Should().NotBe(hash2);
    }
}
