using Plataforma.Aplicacao.Cadastro;

namespace Plataforma.Aplicacao.Negocios;

/// <summary>
/// Trocar o link público (slug) depois do cadastro (seção 5, "Editar o link depois do cadastro"). Sempre o negócio do
/// <c>IContextoNegocio</c>; só o Administrador chega aqui (policy no controller).
/// </summary>
public interface IGerenciadorLinkNegocio
{
    Task<LinkNegocio?> ObterAsync(CancellationToken cancellationToken = default);

    /// <summary>Mesmas regras do cadastro, vistas por este negócio: o próprio link antigo pode voltar; o atual, não.</summary>
    Task<DisponibilidadeSlug> VerificarAsync(string slug, CancellationToken cancellationToken = default);

    Task<ResultadoTrocaLink> TrocarAsync(string slug, CancellationToken cancellationToken = default);
}

/// <param name="ProximaTrocaEm">Nulo = pode trocar agora.</param>
public sealed record LinkNegocio(string Slug, string Url, DateTimeOffset? UltimaTrocaEm, DateTimeOffset? ProximaTrocaEm);

public enum ErroTrocaLink
{
    Invalido,
    EmUso,
    TrocaRecente,
}

public sealed record ResultadoTrocaLink(LinkNegocio? Link, ErroTrocaLink? Erro, string? Mensagem, DateTimeOffset? PodeTrocarEm = null)
{
    public static ResultadoTrocaLink Ok(LinkNegocio link) => new(link, null, null);

    public static ResultadoTrocaLink Falha(ErroTrocaLink erro, string mensagem, DateTimeOffset? podeTrocarEm = null) =>
        new(null, erro, mensagem, podeTrocarEm);
}
