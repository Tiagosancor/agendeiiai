using FluentAssertions;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.WhatsAppAgente;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>Fase 4, Fatia 4: formato fechado dos DTOs de leitura do painel (G16: a lista não carrega texto) e padrões aditivos.</summary>
public sealed class PainelLeituraWhatsAppUnidadeTestes
{
    private static string[] Propriedades<T>() => typeof(T).GetProperties().Select(p => p.Name).ToArray();

    [Fact]
    public void Resumo_da_lista_tem_campos_fechados_e_nenhum_de_texto_de_mensagem()
    {
        Propriedades<ConversaWhatsAppResumo>().Should().BeEquivalentTo(
            "Id", "Telefone", "ClienteNome", "Status", "HumanoDesde", "HumanoMotivo", "HumanoAte", "UltimaMensagemEm", "MensagensDesdeHumano",
            "AguardandoAtendimento");
        Propriedades<PaginaConversasWhatsApp>().Should().BeEquivalentTo("Itens", "Total", "Pagina", "TamanhoPagina");
    }

    [Fact]
    public void Mensagem_do_detalhe_so_tem_id_papel_texto_e_instante()
    {
        Propriedades<MensagemConversaWhatsApp>().Should().BeEquivalentTo("Id", "Papel", "Texto", "Em");
        Propriedades<DetalheConversaWhatsApp>().Should().Contain(["Mensagens", "HaMaisAntigas"]);
    }

    [Fact]
    public void Status_novo_e_aditivo_e_os_padroes_mantem_o_comportamento_anterior()
    {
        var status = new StatusWhatsApp("Conectada", true, "5571999990000", true);

        status.MensagemTransferencia.Should().BeNull();
        status.IaAtiva.Should().BeFalse();
        status.AgendamentoAtivo.Should().BeFalse();
        status.GestaoModo.Should().Be("Desligado");
        status.AtendimentoHumanoAtivo.Should().BeFalse();
    }

    [Fact]
    public void Resumo_da_agenda_ganha_origem_e_reserva_sem_quebrar_quem_nao_informa()
    {
        var resumo = new AgendamentoResumo(
            Guid.NewGuid(), Guid.NewGuid(), null, "Fulano", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "Agendado", null, ["Corte"], 50m);

        resumo.Origem.Should().Be("Painel");
        resumo.ReservadoAte.Should().BeNull();
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("  ", 0)]
    public void Mensagem_de_transferencia_vazia_tira_o_texto(string? texto, int tamanho)
    {
        var instancia = new InstanciaWhatsApp(Guid.NewGuid(), "neg-x");
        instancia.DefinirMensagemTransferencia("antes");

        instancia.DefinirMensagemTransferencia(texto);

        (instancia.MensagemTransferencia?.Length ?? 0).Should().Be(tamanho);
    }

    [Fact]
    public void Mensagem_de_transferencia_aceita_300_e_recusa_301_ou_controle()
    {
        var instancia = new InstanciaWhatsApp(Guid.NewGuid(), "neg-x");

        instancia.DefinirMensagemTransferencia(new string('a', 300));
        instancia.MensagemTransferencia!.Length.Should().Be(300);
        FluentActions.Invoking(() => instancia.DefinirMensagemTransferencia(new string('a', 301))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => instancia.DefinirMensagemTransferencia("oi\u0007")).Should().Throw<ArgumentException>();
        instancia.MensagemTransferencia!.Length.Should().Be(300, "o valor anterior fica quando o novo é recusado");
    }
}
