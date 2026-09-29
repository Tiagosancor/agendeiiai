namespace Plataforma.Aplicacao.Agendamentos;

/// <summary>
/// Visão "Semana" da agenda (seção 7): um profissional por vez, em colunas de dia, com o expediente, os bloqueios e os
/// agendamentos de cada dia.
/// </summary>
public interface IConsultaAgendaSemana
{
    /// <summary>Nulo: profissional não encontrado. <paramref name="inicio"/> é o primeiro dos 7 dias.</summary>
    Task<AgendaSemana?> ObterAsync(Guid profissionalId, DateOnly inicio, CancellationToken cancellationToken = default);
}

/// <summary><c>Turnos</c> e <c>Bloqueios</c> já no fuso do negócio ("09:00–12:00"); <c>Folga</c>: sem turno nesse dia da semana.</summary>
public sealed record DiaAgendaSemana(
    DateOnly Data, bool Folga, IReadOnlyList<string> Turnos, IReadOnlyList<string> Bloqueios, IReadOnlyList<AgendamentoResumo> Agendamentos);

public sealed record AgendaSemana(Guid ProfissionalId, string Nome, DateOnly Inicio, IReadOnlyList<DiaAgendaSemana> Dias);

/// <summary>
/// "Minha agenda" (seção 7): o Profissional logado vê a própria agenda e toca os próprios atendimentos (iniciar,
/// concluir, faltou) sem "gerenciar agenda". O profissional vem sempre do usuário do token, nunca de parâmetro.
/// </summary>
public interface IAgendaDoProfissionalLogado
{
    /// <summary>Nulo: o usuário logado não está vinculado a um profissional.</summary>
    Task<Guid?> MeuProfissionalAsync(CancellationToken cancellationToken = default);

    /// <summary>O agendamento existe e é do profissional vinculado ao usuário logado.</summary>
    Task<bool> EhMeuAsync(Guid agendamentoId, CancellationToken cancellationToken = default);
}
