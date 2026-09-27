using Microsoft.EntityFrameworkCore;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Notificacoes;

/// <summary>
/// A instância pode avisar "entregue" antes de a API terminar de gravar o ID da mensagem no
/// registro de origem (o webhook guarda o evento mesmo sem achar o registro). Quem grava o ID
/// chama isto logo depois, para não perder esse status.
/// </summary>
internal static class EventosWhatsAppRecebidos
{
    public static async Task<IReadOnlyList<StatusCanal>> ListarAsync(
        PlataformaDbContext dbContext, string? idMensagem, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idMensagem))
            return [];

        return await dbContext.EventosWebhookWhatsApp.AsNoTracking()
            .Where(e => e.IdMensagem == idMensagem)
            .OrderBy(e => e.CriadoEm)
            .Select(e => e.Status)
            .ToListAsync(cancellationToken);
    }
}
