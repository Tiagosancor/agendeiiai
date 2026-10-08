using Plataforma.Dominio.Profissionais;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Regras de horário puras (sem banco) compartilhadas por criar, mover e transferir — as mesmas que a
/// disponibilidade aplica ao montar os horários oferecidos, para o POST nunca aceitar o que a lista não oferece.
/// </summary>
public static class RegrasDeHorario
{
    /// <summary>Folga para o "agora" do encaixe não ser recusado como passado (seção 7).</summary>
    public static readonly TimeSpan ToleranciaAgora = TimeSpan.FromMinutes(5);

    /// <summary>Início anterior a "agora" além da tolerância. Compara instantes, nunca relógio local.</summary>
    public static bool JaPassou(DateTimeOffset inicio, DateTimeOffset agora) => inicio < agora - ToleranciaAgora;

    /// <summary>
    /// O intervalo [<paramref name="inicio"/>, <paramref name="fim"/>) cabe inteiro num único turno do dia local
    /// de <paramref name="diaLocal"/>. Os limites do turno viram instantes naquele dia; comparar só a hora do dia
    /// deixava passar quem termina no dia seguinte (23:45 + 30 min = 00:15 "cabia" num turno até 18:00).
    /// </summary>
    public static bool CabeNoExpediente(
        IEnumerable<HorarioTrabalho> turnosDoDia, DateOnly diaLocal, DateTimeOffset inicio, DateTimeOffset fim, TimeZoneInfo fuso) =>
        turnosDoDia.Any(t =>
            ConversorFusoHorario.ParaUtc(diaLocal, t.Inicio, fuso) <= inicio
            && fim <= ConversorFusoHorario.ParaUtc(diaLocal, t.Fim, fuso));
}
