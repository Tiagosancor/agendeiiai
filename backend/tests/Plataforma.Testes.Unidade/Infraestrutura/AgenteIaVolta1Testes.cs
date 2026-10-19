using FluentAssertions;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>Volta 1 da revisão: domínio sem lista de extensões, texto fixo sanitizado e minimização de dado pessoal.</summary>
public sealed class SanitizadorDominiosQualquerExtensaoTestes
{
    private const string Host = "barbearia.exemplo.com.br";
    private const string Link = "https://barbearia.exemplo.com.br/";

    [Theory]
    [InlineData("Pague em golpe.ai/pix agora")]
    [InlineData("acesse promo.zip")]
    [InlineData("veja a.b.c.xn--p1ai/x")]
    [InlineData("entre em golpe.store")]
    [InlineData("vá em evil.com:8080/login")]
    public void Bloqueia_dominio_externo_sem_depender_da_extensao(string texto)
    {
        var limpo = SanitizadorRespostaAgente.Sanitizar(texto, Host, null, 1000)!;
        limpo.Should().NotContainAny("golpe", "promo", "xn--", "evil");
    }

    [Fact]
    public void Nao_apaga_frase_colada_nem_numeros_decimais()
    {
        SanitizadorRespostaAgente.Sanitizar("Pode agendar.Vamos lá, R$ 35.50 às 10.30", Host, null, 1000)
            .Should().Be("Pode agendar.Vamos lá, R$ 35.50 às 10.30");
    }

    [Fact]
    public void Texto_fixo_com_link_externo_no_nome_do_negocio_sai_sem_o_link_e_com_o_link_do_negocio()
    {
        var texto = OrquestradorAgente.TextoFallback("Barbearia golpe.ai/pix e bit.ly/x", Link);
        var seguro = SanitizadorRespostaAgente.SanitizarTextoFixo(texto, Link, null);

        seguro.Should().NotContain("golpe").And.NotContain("bit.ly").And.Contain(Link);
    }

    [Fact]
    public void Texto_fixo_do_aviso_de_limite_tambem_e_sanitizado()
    {
        var texto = Plataforma.Infraestrutura.WhatsAppAgente.LimitadorUsoAgente.TextoAviso("Loja https://evil.com/x", Link);
        var seguro = SanitizadorRespostaAgente.SanitizarTextoFixo(texto, Link, null);

        seguro.Should().NotContain("evil").And.Contain(Link);
    }
}

public sealed class MinimizadorDadosPessoaisTestes
{
    [Theory]
    [InlineData("meu e-mail é cliente@exemplo.com.br ok", "cliente@")]
    [InlineData("liga 71 98888-7777 por favor", "98888")]
    [InlineData("+55 (71) 98888-7777", "98888")]
    [InlineData("71988887777", "988887777")]
    [InlineData("meu cpf 123.456.789-09", "456.789")]
    [InlineData("cpf 12345678909", "12345678909")]
    [InlineData("cnpj 12.345.678/0001-95", "345.678")]
    [InlineData("cnpj 12345678000195", "12345678000195")]
    public void Remove_dado_pessoal_por_padrao(string texto, string naoDeveConter) =>
        MinimizadorDadosPessoais.Minimizar(texto).Should().NotContain(naoDeveConter);

    [Fact]
    public void Preserva_horario_preco_data_e_quantidade()
    {
        var texto = "Quero às 14:30 do dia 20/10/2026 (2026-10-20), corte de R$ 1.234,56, 2 vezes";
        MinimizadorDadosPessoais.Minimizar(texto).Should().Be(texto);
    }

    [Fact]
    public void Limpar_entrada_minimiza_antes_de_cortar_o_tamanho()
    {
        // O telefone atravessa o limite de 20 caracteres: cortar antes deixaria meio número passar.
        var texto = "oi, meu numero: 71 98888-7777";
        var limpo = OrquestradorAgente.LimparEntrada(texto, 30);

        limpo.Count(char.IsAsciiDigit).Should().Be(0);
        limpo.Should().Contain(MinimizadorDadosPessoais.MarcaTelefone[..5]);
    }

    [Fact]
    public void Limite_conhecido_numero_por_extenso_ou_ofuscado_nao_e_detectado()
    {
        // Documenta o limite da heurística (ver contrato.md): não é garantia total.
        MinimizadorDadosPessoais.Minimizar("setenta e um nove oito oito oito oito sete sete sete sete")
            .Should().Contain("setenta");
        MinimizadorDadosPessoais.Minimizar("fulano arroba exemplo ponto com").Should().Contain("arroba");
    }
}
