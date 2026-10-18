using Microsoft.EntityFrameworkCore;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// A trava de agenda do profissional (<c>pg_advisory_xact_lock</c>, liberada no fim da transação): criar, remarcar, transferir e
/// excluir o profissional tomam a MESMA chave. Quem chega depois enxerga o que o anterior confirmou — um agendamento novo não
/// entra num profissional que está sendo excluído, e a exclusão não passa por cima de um agendamento que acabou de ser gravado.
/// Ordem global de travas: agenda do profissional → linha do agendamento (nunca o inverso).
/// </summary>
internal static class TravaDeAgenda
{
    public static Task TravarProfissionalAsync(PlataformaDbContext dbContext, Guid profissionalId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({profissionalId.ToString()}, 0))", cancellationToken);
}
