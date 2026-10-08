using FluentAssertions;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Infraestrutura.Agendamentos;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

public sealed class RegrasDeHorarioTestes
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeZoneInfo Lisboa = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
    private static readonly DateOnly Dia = new(2030, 1, 7); // segunda-feira
    private static readonly DateOnly DiaDeVerao = new(2030, 7, 8); // segunda-feira, horário de verão em Lisboa

    private static DateTimeOffset Local(TimeZoneInfo fuso, DateOnly dia, int hora, int minuto)
    {
        var local = dia.ToDateTime(new TimeOnly(hora, minuto), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, fuso.GetUtcOffset(local)).ToUniversalTime();
    }

    private static HorarioTrabalho Turno(int horaInicio, int minInicio, int horaFim, int minFim) =>
        HorarioTrabalho.Criar(Guid.NewGuid(), Guid.NewGuid(), DiaSemana.Segunda, new TimeOnly(horaInicio, minInicio), new TimeOnly(horaFim, minFim));

    private static readonly HorarioTrabalho[] ManhaETarde = [Turno(9, 0, 12, 0), Turno(13, 0, 18, 0)];

    private static bool Cabe(IEnumerable<HorarioTrabalho> turnos, DateTimeOffset inicio, int duracaoMinutos, TimeZoneInfo? fuso = null, DateOnly? dia = null) =>
        RegrasDeHorario.CabeNoExpediente(turnos, dia ?? Dia, inicio, inicio.AddMinutes(duracaoMinutos), fuso ?? SaoPaulo);

    [Theory]
    [InlineData(9, 0, 30, true)]    // começa junto com o turno
    [InlineData(11, 30, 30, true)]  // termina junto com o turno da manhã
    [InlineData(17, 30, 30, true)]  // termina junto com o expediente
    [InlineData(17, 45, 30, false)] // termina depois do expediente
    [InlineData(8, 45, 30, false)]  // começa antes do expediente
    [InlineData(11, 45, 30, false)] // cai no almoço
    [InlineData(12, 0, 30, false)]  // começa no almoço
    [InlineData(17, 0, 60, true)]   // vários serviços somados: 60 min cabem
    [InlineData(17, 15, 60, false)] // vários serviços somados: 60 min não cabem
    public void Intervalo_inteiro_precisa_caber_num_turno(int hora, int minuto, int duracao, bool esperado) =>
        Cabe(ManhaETarde, Local(SaoPaulo, Dia, hora, minuto), duracao).Should().Be(esperado);

    [Theory]
    [InlineData(23, 45, 30)] // termina 00:15 do dia seguinte: o fim "parece" menor que o fim do turno
    [InlineData(23, 30, 60)] // vários serviços: 60 min terminam 00:30
    [InlineData(18, 0, 30)]
    public void Atendimento_que_termina_no_dia_seguinte_nunca_cabe_num_turno_do_dia(int hora, int minuto, int duracao) =>
        Cabe(ManhaETarde, Local(SaoPaulo, Dia, hora, minuto), duracao).Should().BeFalse();

    [Theory]
    [InlineData(23, 20, 30, true)]   // termina 23:50, dentro do turno
    [InlineData(23, 29, 30, true)]   // termina 23:59, no limite
    [InlineData(23, 50, 30, false)]  // caso de referência: termina 00:20 do dia seguinte
    [InlineData(23, 45, 30, false)]
    public void Turno_ate_23_59_nao_aceita_quem_atravessa_a_meia_noite(int hora, int minuto, int duracao, bool esperado) =>
        Cabe([Turno(0, 0, 23, 59)], Local(SaoPaulo, Dia, hora, minuto), duracao).Should().Be(esperado);

    [Fact]
    public void Sem_turno_no_dia_nada_cabe() =>
        Cabe([], Local(SaoPaulo, Dia, 10, 0), 30).Should().BeFalse();

    [Fact]
    public void Limites_do_turno_seguem_o_fuso_inclusive_no_horario_de_verao()
    {
        // 09:00 em Lisboa no verão = 08:00 UTC; o turno é 09:00–18:00 do relógio de Lisboa.
        Cabe([Turno(9, 0, 18, 0)], Local(Lisboa, DiaDeVerao, 9, 0), 30, Lisboa, DiaDeVerao).Should().BeTrue();
        Cabe([Turno(9, 0, 18, 0)], Local(Lisboa, DiaDeVerao, 8, 59), 30, Lisboa, DiaDeVerao).Should().BeFalse();
        Cabe([Turno(9, 0, 18, 0)], Local(Lisboa, DiaDeVerao, 17, 30), 30, Lisboa, DiaDeVerao).Should().BeTrue();
        Cabe([Turno(9, 0, 18, 0)], Local(Lisboa, DiaDeVerao, 17, 31), 30, Lisboa, DiaDeVerao).Should().BeFalse();
    }

    [Fact]
    public void O_mesmo_instante_com_offsets_diferentes_da_o_mesmo_resultado()
    {
        var emUtc = Local(SaoPaulo, Dia, 10, 0);
        var emSaoPaulo = emUtc.ToOffset(TimeSpan.FromHours(-3));
        var emLisboa = emUtc.ToOffset(TimeSpan.Zero);
        var emTokyo = emUtc.ToOffset(TimeSpan.FromHours(9));

        foreach (var inicio in new[] { emUtc, emSaoPaulo, emLisboa, emTokyo })
            Cabe(ManhaETarde, inicio, 30).Should().BeTrue();
    }

    [Fact]
    public void Passado_alem_da_tolerancia_e_recusado_e_o_agora_do_encaixe_continua_valendo()
    {
        var agora = new DateTimeOffset(2030, 1, 7, 15, 0, 0, TimeSpan.Zero);

        RegrasDeHorario.JaPassou(agora.AddDays(-1), agora).Should().BeTrue();
        RegrasDeHorario.JaPassou(agora.AddMinutes(-30), agora).Should().BeTrue();
        RegrasDeHorario.JaPassou(agora.AddMinutes(-6), agora).Should().BeTrue();
        RegrasDeHorario.JaPassou(agora.AddMinutes(-5), agora).Should().BeFalse(); // limite: ainda é "agora"
        RegrasDeHorario.JaPassou(agora.AddMinutes(-5).AddTicks(-1), agora).Should().BeTrue(); // um tick além do limite
        RegrasDeHorario.JaPassou(agora.AddMinutes(-5).AddTicks(1), agora).Should().BeFalse();
        RegrasDeHorario.JaPassou(agora.AddSeconds(-45), agora).Should().BeFalse(); // "Lançar e iniciar" trunca para o minuto
        RegrasDeHorario.JaPassou(agora, agora).Should().BeFalse();
        RegrasDeHorario.JaPassou(agora.AddHours(1), agora).Should().BeFalse();
    }

    [Fact]
    public void Passado_compara_instantes_e_nao_depende_do_offset_do_texto()
    {
        var agora = new DateTimeOffset(2030, 1, 7, 15, 0, 0, TimeSpan.Zero);
        var ontem = agora.AddDays(-1);

        foreach (var offset in new[] { TimeSpan.Zero, TimeSpan.FromHours(-3), TimeSpan.FromHours(9), TimeSpan.FromHours(-12) })
        {
            RegrasDeHorario.JaPassou(ontem.ToOffset(offset), agora).Should().BeTrue();
            RegrasDeHorario.JaPassou(agora.AddHours(2).ToOffset(offset), agora.ToOffset(offset)).Should().BeFalse();
        }
    }
}
