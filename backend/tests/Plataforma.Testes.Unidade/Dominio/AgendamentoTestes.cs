using FluentAssertions;
using Plataforma.Dominio.Agendamentos;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class AgendamentoTestes
{
    private static readonly ItemServicoAgendamento ServicoDeTeste = new(Guid.NewGuid(), "Corte", 50m, 30);
    private static readonly DateTimeOffset Inicio = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CriarConfirmado_calcula_o_fim_pela_soma_das_duracoes()
    {
        var servicos = new[]
        {
            ServicoDeTeste,
            new ItemServicoAgendamento(Guid.NewGuid(), "Barba", 30m, 20),
        };

        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, servicos);

        agendamento.Fim.Should().Be(Inicio.AddMinutes(50)); // 30 + 20
        agendamento.Status.Should().Be(StatusAgendamento.Agendado);
        agendamento.Servicos.Should().HaveCount(2);
    }

    [Fact]
    public void CriarConfirmado_sem_servicos_lanca_excecao()
    {
        var acao = () => Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, []);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CriarReserva_fica_com_status_Reservado_e_prazo_de_expiracao()
    {
        var agora = Inicio.AddHours(-1);
        var agendamento = Agendamento.CriarReserva(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste], agora, TimeSpan.FromMinutes(10));

        agendamento.Status.Should().Be(StatusAgendamento.Reservado);
        agendamento.ReservadoAte.Should().Be(agora.AddMinutes(10));
    }

    [Fact]
    public void ConfirmarReserva_muda_para_Agendado_e_limpa_o_prazo()
    {
        var agendamento = Agendamento.CriarReserva(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste], Inicio.AddHours(-1), TimeSpan.FromMinutes(10));

        agendamento.ConfirmarReserva();

        agendamento.Status.Should().Be(StatusAgendamento.Agendado);
        agendamento.ReservadoAte.Should().BeNull();
    }

    [Fact]
    public void ConfirmarReserva_em_agendamento_ja_confirmado_lanca_excecao()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);

        var acao = () => agendamento.ConfirmarReserva();

        acao.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Expirar_reserva_muda_para_Expirado()
    {
        var agendamento = Agendamento.CriarReserva(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste], Inicio.AddHours(-1), TimeSpan.FromMinutes(10));

        agendamento.Expirar();

        agendamento.Status.Should().Be(StatusAgendamento.Expirado);
        agendamento.ReservadoAte.Should().BeNull();
    }

    [Fact]
    public void Expirar_um_agendamento_ja_confirmado_nao_faz_nada()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);

        agendamento.Expirar();

        agendamento.Status.Should().Be(StatusAgendamento.Agendado);
    }

    [Fact]
    public void Cancelar_muda_para_Cancelado()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);

        agendamento.Cancelar();

        agendamento.Status.Should().Be(StatusAgendamento.Cancelado);
    }

    [Fact]
    public void Cancelar_um_agendamento_ja_concluido_lanca_excecao()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);
        agendamento.MarcarConcluido();

        var acao = () => agendamento.Cancelar();

        acao.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarcarConcluido_a_partir_de_Agendado_funciona()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);

        agendamento.MarcarConcluido();

        agendamento.Status.Should().Be(StatusAgendamento.Concluido);
    }

    [Fact]
    public void MarcarFaltou_a_partir_de_Agendado_funciona()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);

        agendamento.MarcarFaltou();

        agendamento.Status.Should().Be(StatusAgendamento.Faltou);
    }

    [Fact]
    public void Mover_atualiza_inicio_e_fim()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);
        var novoInicio = Inicio.AddDays(1);
        var novoFim = novoInicio.AddMinutes(30);

        agendamento.Mover(novoInicio, novoFim);

        agendamento.Inicio.Should().Be(novoInicio);
        agendamento.Fim.Should().Be(novoFim);
    }

    [Fact]
    public void Mover_um_agendamento_cancelado_lanca_excecao()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);
        agendamento.Cancelar();

        var acao = () => agendamento.Mover(Inicio.AddDays(1), Inicio.AddDays(1).AddMinutes(30));

        acao.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CriarReserva_publica_aceita_cliente_nulo()
    {
        var agendamento = Agendamento.CriarReserva(
            Guid.NewGuid(), Guid.NewGuid(), null, Inicio, [ServicoDeTeste], Inicio.AddHours(-1), TimeSpan.FromMinutes(10));

        agendamento.ClienteId.Should().BeNull();
        agendamento.Status.Should().Be(StatusAgendamento.Reservado);
    }

    [Fact]
    public void ConfirmarReserva_sem_cliente_vinculado_lanca_excecao()
    {
        var agendamento = Agendamento.CriarReserva(
            Guid.NewGuid(), Guid.NewGuid(), null, Inicio, [ServicoDeTeste], Inicio.AddHours(-1), TimeSpan.FromMinutes(10));

        var acao = () => agendamento.ConfirmarReserva();

        acao.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void VincularCliente_permite_confirmar_depois()
    {
        var clienteId = Guid.NewGuid();
        var agendamento = Agendamento.CriarReserva(
            Guid.NewGuid(), Guid.NewGuid(), null, Inicio, [ServicoDeTeste], Inicio.AddHours(-1), TimeSpan.FromMinutes(10));

        agendamento.VincularCliente(clienteId);
        agendamento.ConfirmarReserva();

        agendamento.ClienteId.Should().Be(clienteId);
        agendamento.Status.Should().Be(StatusAgendamento.Agendado);
    }

    [Fact]
    public void VincularCliente_duas_vezes_lanca_excecao()
    {
        var agendamento = Agendamento.CriarReserva(
            Guid.NewGuid(), Guid.NewGuid(), null, Inicio, [ServicoDeTeste], Inicio.AddHours(-1), TimeSpan.FromMinutes(10));
        agendamento.VincularCliente(Guid.NewGuid());

        var acao = () => agendamento.VincularCliente(Guid.NewGuid());

        acao.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AplicarCupom_reduz_o_total()
    {
        var agendamento = Agendamento.CriarReserva(
            Guid.NewGuid(), Guid.NewGuid(), null, Inicio, [ServicoDeTeste], Inicio.AddHours(-1), TimeSpan.FromMinutes(10));

        agendamento.AplicarCupom(Guid.NewGuid(), 10m);

        agendamento.Total.Should().Be(40m); // 50 - 10
    }

    [Fact]
    public void Total_sem_cupom_e_a_soma_dos_servicos()
    {
        var agendamento = Agendamento.CriarConfirmado(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio,
            [ServicoDeTeste, new ItemServicoAgendamento(Guid.NewGuid(), "Barba", 30m, 20)]);

        agendamento.Total.Should().Be(80m);
    }

    private static readonly TimeSpan UmaHora = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan MeiaHora = TimeSpan.FromMinutes(30);

    [Fact]
    public void Janela_do_lembrete_abre_60_minutos_antes_e_nao_antes_disso()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);

        agendamento.NaJanelaDoLembrete(Inicio.AddMinutes(-61), UmaHora).Should().BeFalse();
        agendamento.NaJanelaDoLembrete(Inicio.AddMinutes(-60), UmaHora).Should().BeTrue();
        agendamento.NaJanelaDoLembrete(Inicio.AddMinutes(-5), UmaHora).Should().BeTrue();
    }

    [Fact]
    public void Janela_fecha_depois_de_enviado()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);
        agendamento.MarcarLembreteEnviado();

        agendamento.NaJanelaDoLembrete(Inicio.AddMinutes(-30), UmaHora).Should().BeFalse();
    }

    [Fact]
    public void Janela_fechada_para_agendamento_que_ja_passou()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);

        agendamento.NaJanelaDoLembrete(Inicio.AddMinutes(1), UmaHora).Should().BeFalse();
    }

    [Fact]
    public void Lembrete_nao_faz_sentido_quando_o_horario_foi_marcado_em_cima_da_hora()
    {
        // Marcado agora (CriadoEm = agora) para daqui a 50 min: o lembrete sairia logo depois da confirmação.
        var emCimaDaHora = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(50), [ServicoDeTeste]);
        emCimaDaHora.LembreteFazSentido(UmaHora, MeiaHora).Should().BeFalse();

        // Marcado agora para daqui a 3 horas: o lembrete chega 2 horas depois — faz sentido.
        var comFolga = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(3), [ServicoDeTeste]);
        comFolga.LembreteFazSentido(UmaHora, MeiaHora).Should().BeTrue();
    }

    [Fact]
    public void Remarcar_zera_o_lembrete_e_conta_a_folga_a_partir_da_remarcacao()
    {
        var agora = DateTimeOffset.UtcNow;
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), agora.AddHours(5), [ServicoDeTeste]);
        agendamento.MarcarLembreteEnviado();

        var novoInicio = agora.AddMinutes(40);
        agendamento.Mover(novoInicio, novoInicio.AddMinutes(30), agora);

        agendamento.LembreteEnviado.Should().BeFalse("o lembrete do horário antigo não vale para o novo");
        agendamento.HorarioCombinadoEm.Should().Be(agora);
        agendamento.LembreteFazSentido(UmaHora, MeiaHora).Should().BeFalse("remarcado para daqui a 40 min, em cima da hora");
    }

    [Fact]
    public void RegistrarConsentimento_grava_data_ip_e_versao()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);
        var agora = DateTimeOffset.UtcNow;

        agendamento.RegistrarConsentimento(agora, "203.0.113.10", "1.0");

        agendamento.ConsentimentoData.Should().Be(agora);
        agendamento.ConsentimentoIp.Should().Be("203.0.113.10");
        agendamento.ConsentimentoVersaoTermos.Should().Be("1.0");
    }

    [Fact]
    public void PrecisaLembrete_falso_para_agendamento_cancelado()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Inicio, [ServicoDeTeste]);
        agendamento.Cancelar();

        agendamento.NaJanelaDoLembrete(Inicio.AddMinutes(-30), UmaHora).Should().BeFalse();
    }
}
