using FluentAssertions;
using Plataforma.Dominio.Usuarios;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class IdentificadorLoginTestes
{
    [Theory]
    [InlineData("ana@teste.com", "ana@teste.com")]
    [InlineData("  ANA@Teste.COM ", "ana@teste.com")]
    public void Com_arroba_e_email(string digitado, string esperado)
    {
        var id = IdentificadorLogin.Interpretar(digitado);
        id.Tipo.Should().Be(TipoIdentificadorLogin.Email);
        id.Email.Should().Be(esperado);
    }

    [Theory]
    [InlineData("(71) 98888-7777")]
    [InlineData("71988887777")]
    [InlineData("7188887777")]              // sem o 9º dígito
    [InlineData("+55 71 8888-7777")]
    [InlineData("5571988887777")]
    [InlineData("+5571988887777")]
    [InlineData("71.98888.7777")]
    public void So_digitos_com_pontuacao_e_telefone_na_forma_canonica(string digitado)
    {
        var id = IdentificadorLogin.Interpretar(digitado);
        id.Tipo.Should().Be(TipoIdentificadorLogin.Telefone);
        id.Telefone!.Valor.Should().Be("+5571988887777");
    }

    [Fact]
    public void Telefone_estrangeiro_fica_como_digitado()
    {
        var id = IdentificadorLogin.Interpretar("+1 555 123 4567");
        id.Tipo.Should().Be(TipoIdentificadorLogin.Telefone);
        id.Telefone!.Valor.Should().Be("+15551234567");
    }

    [Theory]
    [InlineData("joao.silva", "joao.silva")]
    [InlineData("  Joao_2 ", "joao_2")]
    public void Outros_casos_validos_sao_nome_de_usuario(string digitado, string esperado)
    {
        var id = IdentificadorLogin.Interpretar(digitado);
        id.Tipo.Should().Be(TipoIdentificadorLogin.NomeUsuario);
        id.NomeUsuario!.Valor.Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]                   // dígitos que não formam telefone (sem DDD)
    [InlineData("988887777")]
    [InlineData("a")]
    [InlineData("jo ao")]
    [InlineData("+")]
    [InlineData("joao!")]
    public void Entrada_que_nao_serve_e_invalida_sem_lancar(string? digitado) =>
        IdentificadorLogin.Interpretar(digitado).Tipo.Should().Be(TipoIdentificadorLogin.Invalido);
}
