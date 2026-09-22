namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>Converte entre o instante UTC guardado no banco e a hora local do negócio (seção 8.2.6).</summary>
internal static class ConversorFusoHorario
{
    public static DateTimeOffset ParaUtc(DateOnly diaLocal, TimeOnly horaLocal, TimeZoneInfo fuso)
    {
        var dataHoraLocal = diaLocal.ToDateTime(horaLocal, DateTimeKind.Unspecified);
        var offset = fuso.GetUtcOffset(dataHoraLocal);
        return new DateTimeOffset(dataHoraLocal, offset);
    }

    public static (DateOnly Dia, TimeOnly Hora) ParaLocal(DateTimeOffset instanteUtc, TimeZoneInfo fuso)
    {
        var local = TimeZoneInfo.ConvertTime(instanteUtc, fuso);
        return (DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }
}
