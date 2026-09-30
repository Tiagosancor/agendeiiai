using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Cadastro;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Negocios;

/// <summary>
/// Regras de disponibilidade de um slug, as mesmas no cadastro (6.5) e na troca de link (seção 5): formato, reservados, em uso
/// por outro negócio e link antigo de outro negócio ainda redirecionando (90 dias). As mensagens não dizem qual dos dois
/// últimos casos é — para quem tenta, os dois são "em uso".
/// </summary>
public static class DisponibilidadeSlugs
{
    public const string MensagemEmUso = "Este endereço já está em uso. Escolha outro.";
    public const string MensagemReservado = "Este endereço é reservado. Escolha outro.";

    public static readonly string MensagemFormato =
        $"Use de {Slug.TamanhoMinimo} a {Slug.TamanhoMaximo} letras minúsculas, números ou hífen, sem hífen no começo ou no fim.";

    /// <summary>Formato e reservados, sem banco. Devolve a mensagem do problema, ou nulo com o slug válido.</summary>
    public static string? ValidarFormato(string? valor, out Slug? slug)
    {
        slug = null;
        var normalizado = (valor ?? string.Empty).Trim().ToLowerInvariant();

        if (Slug.Reservados.Contains(normalizado))
            return MensagemReservado;

        return Slug.TentarCriar(normalizado, out slug) ? null : MensagemFormato;
    }

    /// <param name="negocioId">Quem está trocando (nulo no cadastro): o próprio link antigo dele não conta como ocupado.</param>
    public static async Task<bool> EmUsoAsync(
        PlataformaDbContext dbContext, Slug slug, Guid? negocioId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var outrosNegocios = dbContext.Negocios.Where(n => n.Slug == slug);
        var linksAntigos = dbContext.SlugsAnteriores.Where(s => s.Slug == slug && s.RedirecionaAte > agora);
        if (negocioId is Guid proprio)
        {
            outrosNegocios = outrosNegocios.Where(n => n.Id != proprio);
            linksAntigos = linksAntigos.Where(s => s.NegocioId != proprio);
        }

        return await outrosNegocios.AnyAsync(cancellationToken) || await linksAntigos.AnyAsync(cancellationToken);
    }

    public static async Task<DisponibilidadeSlug> VerificarAsync(
        PlataformaDbContext dbContext, string? valor, Guid? negocioId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var problema = ValidarFormato(valor, out var slug);
        if (problema is not null)
            return new DisponibilidadeSlug(false, problema);

        return await EmUsoAsync(dbContext, slug!, negocioId, agora, cancellationToken)
            ? new DisponibilidadeSlug(false, MensagemEmUso)
            : new DisponibilidadeSlug(true, null);
    }

    /// <summary>
    /// Enfileira, até o fim da transação, quem disputa o mesmo slug (cadastro × troca de link): o índice único de
    /// <c>negocios.slug</c> não cobre o link antigo de outro negócio, então a checagem e a gravação precisam ser atômicas.
    /// </summary>
    public static Task TravarAsync(PlataformaDbContext dbContext, Slug slug, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"slug:" + slug.Valor}))", cancellationToken);
}
