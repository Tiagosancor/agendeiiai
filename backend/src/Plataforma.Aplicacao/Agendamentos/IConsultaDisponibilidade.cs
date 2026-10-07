namespace Plataforma.Aplicacao.Agendamentos;

/// <summary>
/// Horários livres de um profissional num dia, considerando expediente, almoço,
/// bloqueios e agendamentos existentes (seção 6.2.2 / 8.2). A grade é de 15 minutos
/// (configurável) — mas só oferece o horário se a duração total dos serviços couber
/// inteira num intervalo aberto.
/// </summary>
public interface IConsultaDisponibilidade
{
    /// <param name="ignorarAgendamentoId">
    /// Agendamento que não conta como ocupação — o que está sendo remarcado, para o horário dele
    /// não aparecer indisponível por causa dele mesmo. Os demais conflitos continuam valendo.
    /// </param>
    Task<IReadOnlyList<DateTimeOffset>> ListarHorariosLivresAsync(
        Guid profissionalId, DateOnly data, int duracaoTotalMinutos, CancellationToken cancellationToken = default,
        Guid? ignorarAgendamentoId = null);
}
