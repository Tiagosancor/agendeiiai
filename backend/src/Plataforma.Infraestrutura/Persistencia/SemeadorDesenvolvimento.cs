using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Infraestrutura.Persistencia;

/// <summary>
/// Semeia um negócio de exemplo ("acme") e um administrador, só em desenvolvimento, para
/// que <c>docker compose up</c> já suba com algo para testar manualmente: a resolução por
/// subdomínio (seção 11, Sprint 0) e o login do painel (Sprint 1). Nunca roda em produção —
/// só é chamado a partir do bloco `IsDevelopment()` do <c>Program.cs</c>, do mesmo jeito
/// que a aplicação automática de migrations.
/// </summary>
public static class SemeadorDesenvolvimento
{
    public const string EmailAdministradorDev = "admin@acme.dev";
    public const string SenhaAdministradorDev = "Admin!123";

    public static async Task SemearAsync(
        PlataformaDbContext dbContext, ISenhaHasher senhaHasher, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Negocios.AnyAsync(cancellationToken))
            return;

        var negocio = Negocio.Criar(Slug.Criar("acme"), "Acme Barbearia (dev)", TipoNegocio.Barbearia);
        dbContext.Negocios.Add(negocio);

        var administrador = Usuario.Criar(
            negocio.Id, "Administrador (dev)", EmailAdministradorDev, Perfil.Administrador,
            senhaHasher.Hash(SenhaAdministradorDev));
        dbContext.Usuarios.Add(administrador);

        var plano = await dbContext.Planos.OrderBy(p => p.Ordem).FirstAsync(cancellationToken);
        dbContext.Assinaturas.Add(ServicoAssinatura.IniciarTeste(
            negocio.Id, plano, Periodicidade.Mensal, "semeador-dev", DateTimeOffset.UtcNow));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
