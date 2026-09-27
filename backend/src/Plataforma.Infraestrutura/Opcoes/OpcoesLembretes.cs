using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>Lembrete ao cliente antes do agendamento (seção 9) — um só, antecedência configurável (padrão 60 min).</summary>
public sealed class OpcoesLembretes
{
    public const string Secao = "Lembretes";

    /// <summary>Quanto tempo antes do horário o lembrete sai (<c>Lembretes__AntecedenciaMinutos</c>).</summary>
    [Range(5, 10080)]
    public int AntecedenciaMinutos { get; set; } = 60;

    /// <summary>
    /// Folga mínima entre a marcação e o lembrete: se ele fosse chegar antes disso, não é enviado
    /// (a confirmação acabou de sair). Padrão 30 min — com a antecedência de 60, quem marca a menos
    /// de 1h30 do horário não recebe lembrete.
    /// </summary>
    [Range(0, 1440)]
    public int IntervaloMinimoAposMarcarMinutos { get; set; } = 30;
}
