using FluentAssertions;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>Fase 4, fatia 2: máscara do link de gestão (G3), anonimização da conversa (G11) e configuração da retenção.</summary>
public sealed class LgpdConversasUnidadeTestes
{
    private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2VjcmV0YXNzaW5hdHVyYQ";

    [Theory]
    [InlineData("https://acme.agendeiiai.com.br/agendamentos/TOKEN123abc", "[link de gestão removido]")]
    [InlineData("veja acme.agendeiiai.localhost:3000/agendamentos/TOKEN123abc, ok", "veja [link de gestão removido], ok")]
    [InlineData("HTTP://x.com/AGENDAMENTOS/abc", "[link de gestão removido]")]
    [InlineData("/agendamentos/abc123", "[link de gestão removido]")]
    [InlineData("meu token " + Jwt + " tá aqui", "meu token [link de gestão removido] tá aqui")]
    [InlineData("https://acme.agendeiiai.com.br/agendamentos/" + Jwt, "[link de gestão removido]")]
    [InlineData("quero agendar amanhã às 10h", "quero agendar amanhã às 10h")]
    [InlineData("https://acme.agendeiiai.com.br/", "https://acme.agendeiiai.com.br/")]
    public void Mascara_o_link_de_gestao_e_o_jwt(string entrada, string esperado) =>
        MascaraLinkGestao.Aplicar(entrada).Should().Be(esperado);

    [Fact]
    public void Minimizador_tambem_mascara_o_token_antes_da_ia() =>
        MinimizadorDadosPessoais.Minimizar("cancele https://a.com/agendamentos/" + Jwt).Should().NotContain("eyJ").And.NotContain("/agendamentos/");

    [Fact]
    public void Anonimizar_conversa_troca_telefone_limpa_vinculo_e_resumo_e_solta_o_lease()
    {
        var conversa = new ConversaWhatsApp(Guid.NewGuid(), Guid.NewGuid(), "+5571988887777", DateTimeOffset.UtcNow);
        conversa.VincularCliente(Guid.NewGuid());

        conversa.Anonimizar(new HashSet<string>());

        conversa.TelefoneCliente.Should().StartWith("anonimizado:").And.HaveLength(20);
        conversa.Anonimizada.Should().BeTrue();
        conversa.ClienteId.Should().BeNull();
        conversa.Resumo.Should().BeNull();
        conversa.Status.Should().Be(StatusConversaWhatsApp.Agente);
        conversa.ProcessandoAte.Should().BeNull();
    }

    [Fact]
    public void Anonimizar_conversa_evita_colisao_no_indice_unico()
    {
        var conversa = new ConversaWhatsApp(Guid.NewGuid(), Guid.NewGuid(), "+5571988887777", DateTimeOffset.UtcNow);
        var primeiro = ConversaWhatsApp.PrefixoTelefoneAnonimizado + conversa.Id.ToString("N")[..8];

        var emUso = new HashSet<string> { primeiro };
        conversa.Anonimizar(emUso);

        conversa.TelefoneCliente.Should().NotBe(primeiro).And.HaveLength(20);
        emUso.Should().Contain(conversa.TelefoneCliente);
    }

    [Fact]
    public void Retencao_de_mensagens_vem_desligada_e_aceita_zero_ou_30_a_730()
    {
        var validador = new ValidadorOpcoesIa();
        new OpcoesIa().RetencaoMensagensDias.Should().Be(0);
        validador.Validate(null, new OpcoesIa()).Succeeded.Should().BeTrue();

        foreach (var dias in new[] { 0, 30, 180, 730 })
            validador.Validate(null, new OpcoesIa { RetencaoMensagensDias = dias }).Succeeded.Should().BeTrue();
        foreach (var dias in new[] { -1, 1, 29, 731 })
            validador.Validate(null, new OpcoesIa { RetencaoMensagensDias = dias }).Failed.Should().BeTrue();
    }
}

/// <summary>Contrato serializado da exportação (conversas): nomes e capitalização que o contrato.md promete.</summary>
public sealed class ContratoExportacaoConversasTestes
{
    [Fact]
    public void Exportacao_serializa_conversas_em_camelCase_com_papel_minusculo()
    {
        var em = new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero);
        var exportacao = new Plataforma.Aplicacao.Clientes.ExportacaoCliente(
            Guid.NewGuid(), "Maria", "+5571988887777", null, null, "WhatsApp", em, [],
            [new Plataforma.Aplicacao.WhatsAppAgente.ExportacaoConversaWhatsApp(em, [new Plataforma.Aplicacao.WhatsAppAgente.ExportacaoMensagemWhatsApp(em, "cliente", "Oi")])]);

        var json = System.Text.Json.JsonSerializer.Serialize(exportacao, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var conversa = doc.RootElement.GetProperty("conversas")[0];
        conversa.TryGetProperty("ultimaMensagemEm", out _).Should().BeTrue();
        var mensagem = conversa.GetProperty("mensagens")[0];
        mensagem.GetProperty("papel").GetString().Should().Be("cliente");
        mensagem.GetProperty("texto").GetString().Should().Be("Oi");
        mensagem.TryGetProperty("em", out _).Should().BeTrue();
    }
}
