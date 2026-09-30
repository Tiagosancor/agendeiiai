using Plataforma.Dominio.Negocios;

namespace Plataforma.Aplicacao.Negocios;

/// <summary>Consulta somente-leitura de negócios para uso em rotas anônimas (resolução por subdomínio).</summary>
public interface IConsultaNegocioPublico
{
    /// <summary>Busca um negócio ativo pelo slug. Retorna <c>null</c> se não existir ou estiver inativo.</summary>
    Task<NegocioResumo?> ObterPorSlugAsync(Slug slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Link atual de quem usava <paramref name="antigo"/> e trocou há menos de 90 dias (seção 5: o endereço antigo redireciona).
    /// Nulo depois do prazo, ou se o negócio foi desativado.
    /// </summary>
    Task<string?> SlugAtualDoAnteriorAsync(Slug antigo, CancellationToken cancellationToken = default);
}
