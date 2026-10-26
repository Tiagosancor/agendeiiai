using System.Text.Json;
using FluentAssertions;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

public sealed class JidDoTelefoneTestes
{
    private static string? Jid(string chaveJson) => ProcessadorWebhookAgente.JidDoTelefone(JsonDocument.Parse(chaveJson).RootElement);

    [Fact]
    public void Individual_normal_e_aceito() =>
        Jid("""{"remoteJid":"5571988887777@s.whatsapp.net"}""").Should().Be("5571988887777@s.whatsapp.net");

    [Fact]
    public void Aparelho_vinculado_perde_o_sufixo_do_aparelho() =>
        Jid("""{"remoteJid":"5571988887777:12@s.whatsapp.net"}""").Should().Be("5571988887777@s.whatsapp.net");

    [Fact]
    public void Lid_com_senderPn_e_aceito() =>
        Jid("""{"remoteJid":"123456@lid","senderPn":"5571988887777@s.whatsapp.net"}""").Should().Be("5571988887777@s.whatsapp.net");

    [Fact]
    public void Lid_com_remoteJidAlt_e_aceito() =>
        Jid("""{"remoteJid":"123456@lid","remoteJidAlt":"5571988887777@s.whatsapp.net"}""").Should().Be("5571988887777@s.whatsapp.net");

    [Fact]
    public void Lid_sem_numero_e_ignorado() => Jid("""{"remoteJid":"123456@lid"}""").Should().BeNull();

    [Theory]
    [InlineData("1203630@g.us")]
    [InlineData("status@broadcast")]
    [InlineData("5571988887777@broadcast")]
    [InlineData("1203630@newsletter")]
    public void Grupo_difusao_status_e_canal_sao_ignorados_mesmo_com_senderPn(string remoto) =>
        Jid($$"""{"remoteJid":"{{remoto}}","senderPn":"5571988887777@s.whatsapp.net","remoteJidAlt":"5571988887777@s.whatsapp.net"}""")
            .Should().BeNull();

    [Fact]
    public void Valores_novos_dos_enums_viram_texto_e_voltam()
    {
        JsonSerializer.Serialize(OrigemAgendamento.WhatsApp, new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })
            .Should().Be("\"WhatsApp\"");
        Enum.Parse<OrigemAgendamento>("WhatsApp").Should().Be(OrigemAgendamento.WhatsApp);
        Enum.Parse<OrigemCliente>("WhatsApp").Should().Be(OrigemCliente.WhatsApp);
        ((int)OrigemAgendamento.WhatsApp).Should().Be(4);
        ((int)OrigemCliente.WhatsApp).Should().Be(4);
        OrigemAgendamento.WhatsApp.ToString().Length.Should().BeLessThanOrEqualTo(20);
    }
}

public sealed class NonoDigitoTestes
{
    private static string[] Formas(string telefone) => TelefoneE164.Criar(telefone).FormasEquivalentes().Select(t => t.Valor).ToArray();

    [Theory]
    [InlineData("+5571988887777", new[] { "+5571988887777", "+557188887777" })]
    [InlineData("+557188887777", new[] { "+5571988887777", "+557188887777" })]
    [InlineData("+5511999998888", new[] { "+5511999998888", "+551199998888" })]
    [InlineData("+551165432100", new[] { "+5511965432100", "+551165432100" })]
    public void Celular_devolve_as_duas_formas_com_13_digitos_primeiro(string entrada, string[] esperado) =>
        Formas(entrada).Should().Equal(esperado);

    [Theory]
    [InlineData("+557132345678")] // fixo (assinante começa com 3)
    [InlineData("+551125551234")] // fixo
    [InlineData("+5571922221111")] // com 9, mas o resto começa com 2: não existe forma sem 9
    [InlineData("+14155552671")] // outro país
    [InlineData("+5571988887")] // curto
    [InlineData("+55719888877777")] // longo
    public void Fixo_estrangeiro_e_formato_estranho_ficam_como_estao(string entrada) =>
        Formas(entrada).Should().Equal(entrada);

    [Fact]
    public void Entrada_sem_mais_no_estilo_brasileiro_funciona() =>
        Formas("(71) 98888-7777").Should().Equal("+5571988887777", "+557188887777");

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12")]
    public void Entradas_invalidas_nem_viram_telefone(string entrada) =>
        TelefoneE164.TentarCriar(entrada, out _).Should().BeFalse();
}
