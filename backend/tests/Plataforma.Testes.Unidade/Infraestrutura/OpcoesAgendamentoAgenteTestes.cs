using FluentAssertions;
using Plataforma.Infraestrutura.Opcoes;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

public sealed class OpcoesAgendamentoAgenteTestes
{
    private static readonly ValidadorOpcoesIa Validador = new();

    [Fact]
    public void Padrao_e_desligado_com_os_limites_do_plano()
    {
        var o = new OpcoesIa();

        o.Agendamento.Modo.Should().Be(ModoAgendamentoAgente.Desligado);
        (o.Agendamento.LimitePreparosPorTelefonePorDia, o.Agendamento.LimiteFuturosPorTelefone,
            o.Agendamento.LimiteConfirmacoesPorTelefonePorDia, o.Agendamento.LimiteReservasAtivasPorNegocio, o.AvisoAutomatizadoDias)
            .Should().Be((5, 3, 3, 20, 90));
        o.AgendamentoAtivoPara(Guid.NewGuid()).Should().BeFalse();
        Validador.Validate(null, o).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Piloto_com_lista_vazia_e_nenhum_negocio_e_todos_liga_geral()
    {
        var negocio = Guid.NewGuid();
        var piloto = new OpcoesAgendamentoAgente { Modo = ModoAgendamentoAgente.Piloto };
        piloto.Habilitado(negocio).Should().BeFalse();

        piloto.Negocios = $"{Guid.NewGuid()}, {negocio}";
        piloto.Habilitado(negocio).Should().BeTrue();
        piloto.Habilitado(Guid.NewGuid()).Should().BeFalse();

        new OpcoesAgendamentoAgente { Modo = ModoAgendamentoAgente.Todos }.Habilitado(Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void So_vale_com_Gemini_e_dentro_do_piloto_da_fase_2()
    {
        var negocio = Guid.NewGuid();
        var o = new OpcoesIa { Provider = ProvedorIa.Eco, Agendamento = { Modo = ModoAgendamentoAgente.Todos } };
        o.AgendamentoAtivoPara(negocio).Should().BeFalse();

        o.Provider = ProvedorIa.Gemini;
        o.AgendamentoAtivoPara(negocio).Should().BeTrue();

        o.NegociosHabilitados = Guid.NewGuid().ToString();
        o.AgendamentoAtivoPara(negocio).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Preparos_por_dia_entre_1_e_100(int v) =>
        Validador.Validate(null, new OpcoesIa { Agendamento = { LimitePreparosPorTelefonePorDia = v } }).Failed.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Futuros_e_confirmacoes_entre_1_e_50(int v)
    {
        Validador.Validate(null, new OpcoesIa { Agendamento = { LimiteFuturosPorTelefone = v } }).Failed.Should().BeTrue();
        Validador.Validate(null, new OpcoesIa { Agendamento = { LimiteConfirmacoesPorTelefonePorDia = v } }).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Reservas_ativas_entre_1_e_1000(int v) =>
        Validador.Validate(null, new OpcoesIa { Agendamento = { LimiteReservasAtivasPorNegocio = v } }).Failed.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Aviso_automatizado_entre_1_e_365_dias(int v) =>
        Validador.Validate(null, new OpcoesIa { AvisoAutomatizadoDias = v }).Failed.Should().BeTrue();

    [Fact]
    public void Modo_indefinido_e_lista_que_nao_e_guid_sao_recusados()
    {
        Validador.Validate(null, new OpcoesIa { Agendamento = { Modo = (ModoAgendamentoAgente)9 } }).Failed.Should().BeTrue();
        Validador.Validate(null, new OpcoesIa { Agendamento = { Negocios = "abc" } }).Failed.Should().BeTrue();
    }
}
