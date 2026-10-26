using FluentAssertions;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.WhatsAppAgente;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class PendenciaAgendamentoWhatsAppTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static PendenciaAgendamentoWhatsApp Nova(string? nome = "Maria") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Agora, Agora.AddMinutes(10), nome);

    [Fact]
    public void Nasce_aguardando_com_o_nome_aparado()
    {
        var p = Nova("  Maria  ");

        p.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
        p.Aguardando.Should().BeTrue();
        p.NomeInformado.Should().Be("Maria");
        p.ConcluidaEm.Should().BeNull();
    }

    [Fact]
    public void Nome_so_com_espacos_vira_nulo_e_nome_grande_demais_e_recusado()
    {
        Nova("   ").NomeInformado.Should().BeNull();

        var acao = () => Nova(new string('a', 81));
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Expiracao_precisa_ser_depois_da_criacao()
    {
        var acao = () => new PendenciaAgendamentoWhatsApp(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Agora, Agora);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Vencida_so_a_partir_do_instante_de_expiracao()
    {
        var p = Nova();

        p.Vencida(Agora.AddMinutes(10).AddTicks(-1)).Should().BeFalse();
        p.Vencida(Agora.AddMinutes(10)).Should().BeTrue();
    }

    [Fact]
    public void Confirmar_grava_evidencia_e_apaga_o_nome()
    {
        var p = Nova();
        var resumo = Guid.NewGuid();
        var sim = Guid.NewGuid();

        p.RegistrarResumoEnviado(resumo, Agora.AddSeconds(2));
        p.Confirmar(Agora.AddMinutes(1), sim, "1.0");

        p.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Confirmada);
        p.ResumoMensagemId.Should().Be(resumo);
        p.ConfirmacaoMensagemId.Should().Be(sim);
        p.VersaoTermos.Should().Be("1.0");
        p.ConcluidaEm.Should().Be(Agora.AddMinutes(1));
        p.NomeInformado.Should().BeNull();
    }

    [Theory]
    [InlineData(StatusPendenciaAgendamentoWhatsApp.Recusada)]
    [InlineData(StatusPendenciaAgendamentoWhatsApp.Expirada)]
    [InlineData(StatusPendenciaAgendamentoWhatsApp.Substituida)]
    [InlineData(StatusPendenciaAgendamentoWhatsApp.Falhou)]
    public void Cada_encerramento_grava_o_estado_e_limpa_o_nome(StatusPendenciaAgendamentoWhatsApp esperado)
    {
        var p = Nova();

        switch (esperado)
        {
            case StatusPendenciaAgendamentoWhatsApp.Recusada: p.Recusar(Agora); break;
            case StatusPendenciaAgendamentoWhatsApp.Expirada: p.Expirar(Agora); break;
            case StatusPendenciaAgendamentoWhatsApp.Substituida: p.Substituir(Agora); break;
            default: p.Falhar(Agora); break;
        }

        p.Status.Should().Be(esperado);
        p.Aguardando.Should().BeFalse();
        p.NomeInformado.Should().BeNull();
        p.ConcluidaEm.Should().Be(Agora);
    }

    [Fact]
    public void Depois_de_encerrada_nenhuma_transicao_vale()
    {
        var p = Nova();
        p.Expirar(Agora);

        ((Action)(() => p.Confirmar(Agora, Guid.NewGuid(), "1.0"))).Should().Throw<InvalidOperationException>();
        ((Action)(() => p.Recusar(Agora))).Should().Throw<InvalidOperationException>();
        ((Action)(() => p.Substituir(Agora))).Should().Throw<InvalidOperationException>();
        ((Action)(() => p.RegistrarResumoEnviado(Guid.NewGuid(), Agora))).Should().Throw<InvalidOperationException>();
        p.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Expirada);
    }

    [Fact]
    public void Confirmada_nao_volta_atras()
    {
        var p = Nova();
        p.Confirmar(Agora, null, "1.0");

        ((Action)(() => p.Expirar(Agora))).Should().Throw<InvalidOperationException>();
        p.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Confirmada);
    }

    [Fact]
    public void Reserva_criada_com_origem_whatsapp_guarda_a_origem_e_o_padrao_continua_link_publico()
    {
        var itens = new[] { new ItemServicoAgendamento(Guid.NewGuid(), "Corte", 50m, 30) };
        var inicio = Agora.AddDays(1);

        var padrao = Agendamento.CriarReserva(Guid.NewGuid(), Guid.NewGuid(), null, inicio, itens, Agora, TimeSpan.FromMinutes(10));
        var whats = Agendamento.CriarReserva(
            Guid.NewGuid(), Guid.NewGuid(), null, inicio, itens, Agora, TimeSpan.FromMinutes(10), origem: OrigemAgendamento.WhatsApp);

        padrao.Origem.Should().Be(OrigemAgendamento.LinkPublico);
        whats.Origem.Should().Be(OrigemAgendamento.WhatsApp);
        whats.Status.Should().Be(StatusAgendamento.Reservado);
        whats.ClienteId.Should().BeNull();
        whats.PodeReceberMensagens.Should().BeTrue();
    }

    [Fact]
    public void Consentimento_pelo_whatsapp_nao_tem_ip()
    {
        var itens = new[] { new ItemServicoAgendamento(Guid.NewGuid(), "Corte", 50m, 30) };
        var a = Agendamento.CriarReserva(
            Guid.NewGuid(), Guid.NewGuid(), null, Agora.AddDays(1), itens, Agora, TimeSpan.FromMinutes(10), origem: OrigemAgendamento.WhatsApp);

        a.RegistrarConsentimentoWhatsApp(Agora, "1.0");

        a.ConsentimentoData.Should().Be(Agora);
        a.ConsentimentoVersaoTermos.Should().Be("1.0");
        a.ConsentimentoIp.Should().BeNull();
    }

    [Fact]
    public void Conversa_vincula_e_desvincula_o_cliente_e_registra_o_aviso()
    {
        var c = new ConversaWhatsApp(Guid.NewGuid(), Guid.NewGuid(), "+5571988887777", Agora);
        var cliente = Guid.NewGuid();

        c.VincularCliente(cliente);
        c.ClienteId.Should().Be(cliente);
        c.DesvincularCliente();
        c.ClienteId.Should().BeNull();

        c.AvisoAutomatizadoEm.Should().BeNull();
        c.RegistrarAvisoAutomatizado(Agora);
        c.AvisoAutomatizadoEm.Should().Be(Agora);
    }
}
