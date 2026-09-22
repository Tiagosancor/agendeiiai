using System.Text;
using Plataforma.Aplicacao.Ics;

namespace Plataforma.Infraestrutura.Ics;

/// <summary>Arquivo <c>.ics</c> (RFC 5545) mínimo para "Adicionar ao calendário" (seção 6.3) — um único VEVENT, sem dependência externa.</summary>
public sealed class GeradorIcs : IGeradorIcs
{
    public string Gerar(DadosIcs dados)
    {
        var construtor = new StringBuilder();
        construtor.AppendLine("BEGIN:VCALENDAR");
        construtor.AppendLine("VERSION:2.0");
        construtor.AppendLine("PRODID:-//Plataforma//Agendamento//PT-BR");
        construtor.AppendLine("BEGIN:VEVENT");
        construtor.AppendLine($"UID:{dados.AgendamentoId}@plataforma");
        construtor.AppendLine($"DTSTAMP:{FormatarUtc(DateTimeOffset.UtcNow)}");
        construtor.AppendLine($"DTSTART:{FormatarUtc(dados.Inicio)}");
        construtor.AppendLine($"DTEND:{FormatarUtc(dados.Fim)}");
        construtor.AppendLine($"SUMMARY:{Escapar(dados.NomeNegocio)}");
        construtor.AppendLine($"DESCRIPTION:{Escapar(dados.Descricao)}");
        construtor.AppendLine($"LOCATION:{Escapar(dados.Local)}");
        construtor.AppendLine("END:VEVENT");
        construtor.AppendLine("END:VCALENDAR");

        return construtor.ToString();
    }

    private static string FormatarUtc(DateTimeOffset valor) => valor.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");

    private static string Escapar(string valor) =>
        valor.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
}
