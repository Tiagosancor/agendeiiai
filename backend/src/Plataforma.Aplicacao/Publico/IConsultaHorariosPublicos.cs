namespace Plataforma.Aplicacao.Publico;

public sealed record HorarioLivre(DateTimeOffset Inicio, Guid ProfissionalId);

/// <summary>
/// Horários livres como o assistente público os oferece (seção 6.2.2), compartilhado entre o endpoint público e o agente de
/// WhatsApp. Com <c>profissionalId</c> é a disponibilidade de um só; sem ele, agrega os profissionais ativos que executam
/// TODOS os serviços. A duração é a de cada profissional (vínculo); <c>duracaoMinutos</c> só vale para quem não tem vínculo.
/// </summary>
public interface IConsultaHorariosPublicos
{
    Task<IReadOnlyList<HorarioLivre>> ListarAsync(
        DateOnly data, int duracaoMinutos, IReadOnlyList<Guid> servicoIds, Guid? profissionalId, int maximo,
        CancellationToken cancellationToken = default);
}
