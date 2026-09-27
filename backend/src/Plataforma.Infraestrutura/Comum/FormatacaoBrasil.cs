using System.Globalization;

namespace Plataforma.Infraestrutura.Comum;

/// <summary>
/// Datas e valores do jeito brasileiro em e-mails, WhatsApp e textos gerados pelo backend.
/// Formata à mão sobre a cultura invariante: a imagem Alpine roda em globalização invariante e
/// <c>new CultureInfo("pt-BR")</c> lança lá (só em produção/container — os testes no host passam).
/// </summary>
public static class FormatacaoBrasil
{
    /// <summary>"R$ 1.234,56".</summary>
    public static string Reais(decimal valor)
    {
        var invariante = valor.ToString("#,##0.00", CultureInfo.InvariantCulture); // 1,234.56
        return "R$ " + invariante.Replace(",", "\u0001").Replace(".", ",").Replace("\u0001", ".");
    }

    /// <summary>"05/10/2026 às 10:00", na hora local do negócio (o banco guarda UTC — seção 8.2.6).</summary>
    public static string DataHora(DateTimeOffset instante, string fuso) =>
        ParaLocal(instante, fuso).ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"05/10/2026", no dia local do negócio.</summary>
    public static string Data(DateTimeOffset instante, string fuso) =>
        ParaLocal(instante, fuso).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParaLocal(DateTimeOffset instante, string fuso) =>
        TimeZoneInfo.ConvertTime(instante, TimeZoneInfo.FindSystemTimeZoneById(fuso));
}
