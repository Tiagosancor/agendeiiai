using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>Lembrete ao cliente antes do agendamento (seção 9) — antecedência configurável, padrão 24h e 2h.</summary>
public sealed class OpcoesLembretes
{
    public const string Secao = "Lembretes";

    [Range(1, 168)]
    public int AntecedenciaPrimeiroLembreteHoras { get; set; } = 24;

    [Range(1, 168)]
    public int AntecedenciaSegundoLembreteHoras { get; set; } = 2;
}
