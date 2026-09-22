using Microsoft.EntityFrameworkCore;
using Plataforma.Dominio.Negocios;

namespace Plataforma.Infraestrutura.Persistencia;

/// <summary>
/// Semeia um negócio de exemplo ("acme") só em desenvolvimento, para que
/// <c>docker compose up</c> já suba com algo em <c>acme.{dominio}</c> para testar
/// manualmente (seção 11, Sprint 0: "acme.localhost resolve o negócio acme").
/// Nunca roda em produção — só é chamado a partir do bloco `IsDevelopment()` do
/// <c>Program.cs</c>, do mesmo jeito que a aplicação automática de migrations.
/// </summary>
public static class SemeadorDesenvolvimento
{
    public static async Task SemearAsync(PlataformaDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Negocios.AnyAsync(cancellationToken))
            return;

        dbContext.Negocios.Add(
            Negocio.Criar(Slug.Criar("acme"), "Acme Barbearia (dev)", TipoNegocio.Barbearia));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
