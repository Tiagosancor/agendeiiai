using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Negocios;

/// <summary>
/// Horário de funcionamento exibido na página pública (seção 6.1.5) — não confundir com
/// o horário de trabalho por profissional (seção 7 / Sprint 2), que controla a agenda.
/// </summary>
public sealed record HorarioFuncionamentoDia(DiaSemana DiaSemana, TimeOnly? Abertura, TimeOnly? Fechamento, bool Fechado)
{
    public static HorarioFuncionamentoDia CriarFechado(DiaSemana dia) => new(dia, null, null, true);
}
