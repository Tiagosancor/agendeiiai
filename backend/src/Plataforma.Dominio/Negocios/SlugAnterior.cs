using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Negocios;

/// <summary>
/// Link antigo de um negócio que trocou de slug (seção 5, "Editar o link depois do cadastro"). Até <see cref="RedirecionaAte"/>
/// o endereço antigo leva (301) ao atual, e nenhum outro negócio pode ficar com ele — senão sequestraria o tráfego de quem
/// ainda tem o link salvo. Não é <c>IEntidadeDoNegocio</c>: é consultado sem negócio resolvido (o redirecionamento e a
/// checagem de disponibilidade olham todos os negócios). A linha fica depois do prazo, como histórico.
/// </summary>
public sealed class SlugAnterior : EntidadeBase
{
    public const int DiasRedirecionamento = 90;

    public Guid NegocioId { get; private set; }

    public Slug Slug { get; private set; } = null!;

    public DateTimeOffset TrocadoEm { get; private set; }

    public DateTimeOffset RedirecionaAte { get; private set; }

    private SlugAnterior()
    {
    }

    internal SlugAnterior(Guid negocioId, Slug slug, DateTimeOffset trocadoEm)
    {
        NegocioId = negocioId;
        Slug = slug;
        TrocadoEm = trocadoEm;
        RedirecionaAte = trocadoEm.AddDays(DiasRedirecionamento);
    }

    public bool Ativo(DateTimeOffset agora) => agora < RedirecionaAte;
}

/// <summary>Segunda troca de link antes de <see cref="Negocio.DiasEntreTrocasDeSlug"/> dias.</summary>
public sealed class TrocaDeSlugRecenteException(DateTimeOffset podeTrocarEm)
    : Exception("O link só pode ser trocado uma vez a cada 30 dias.")
{
    public DateTimeOffset PodeTrocarEm { get; } = podeTrocarEm;
}
