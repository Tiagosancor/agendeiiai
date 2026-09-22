using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>Regras do link público de cancelar/remarcar (seção 6.3).</summary>
public sealed class OpcoesAgendamentoPublico
{
    public const string Secao = "AgendamentoPublico";

    /// <summary>Antecedência mínima, em horas, para cancelar ou remarcar pelo link do e-mail.</summary>
    [Range(0, 168)]
    public int AntecedenciaMinimaCancelamentoHoras { get; set; } = 2;
}
