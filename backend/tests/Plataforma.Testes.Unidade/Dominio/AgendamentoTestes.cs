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
}
