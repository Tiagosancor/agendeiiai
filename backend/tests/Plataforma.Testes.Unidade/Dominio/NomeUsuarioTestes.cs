using FluentAssertions;
using Plataforma.Dominio.Usuarios;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class NomeUsuarioTestes
{
    [Theory]
    [InlineData("joao", "joao")]
    [InlineData("  Joao.Silva  ", "joao.silva")]
    [InlineData("ana_2", "ana_2")]
    [InlineData("a-b", "a-b")]
    [InlineData("1a2", "1a2")]
    [InlineData("zz-e2e-1234", "zz-e2e-1234")]
    public void Normaliza_e_aceita(string digitado, string esperado) =>
        NomeUsuario.Criar(digitado).Valor.Should().Be(esperado);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]                                  // curto
    [InlineData("abcdefghijklmnopqrstuvwxyz12345")]     // 31
    [InlineData("12345678")]                            // só dígitos: seria confundido com telefone
    [InlineData("1.2-3")]                               // sem letra
    [InlineData(".joao")]
    [InlineData("joao.")]
    [InlineData("-joao")]
    [InlineData("joao_")]
    [InlineData("joao..silva")]
    [InlineData("joao._silva")]
    [InlineData("joão")]                                // não ASCII
    [InlineData("joao silva")]
    [InlineData("joao@silva")]
    [InlineData("+joao")]
    public void Recusa_formato_invalido(string digitado)
    {
        NomeUsuario.Avaliar(digitado).Should().Be(ProblemaNomeUsuario.Invalido);
        NomeUsuario.TentarCriar(digitado, out var nome).Should().BeFalse();
        nome.Should().BeNull();
        var criar = () => NomeUsuario.Criar(digitado);
        criar.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Suporte")]
    [InlineData("root")]
    [InlineData("login")]
    [InlineData("null")]
    [InlineData("api")]
    public void Recusa_reservado(string digitado) =>
        NomeUsuario.Avaliar(digitado).Should().Be(ProblemaNomeUsuario.Reservado);

    [Fact]
    public void Aceita_reservados_extras_vindos_de_quem_chama_como_o_nome_do_produto() =>
        NomeUsuario.Avaliar("Agendeiiai", ["agendeiiai"]).Should().Be(ProblemaNomeUsuario.Reservado);

    [Fact]
    public void Tamanho_nos_limites() =>
        NomeUsuario.Avaliar(new string('a', 30)).Should().Be(ProblemaNomeUsuario.Nenhum);

    [Fact]
    public void Reconstituir_nao_revalida() =>
        NomeUsuario.Reconstituir("admin").Valor.Should().Be("admin");
}
