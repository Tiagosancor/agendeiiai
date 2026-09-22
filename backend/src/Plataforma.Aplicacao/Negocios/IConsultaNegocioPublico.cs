using Plataforma.Dominio.Negocios;

namespace Plataforma.Aplicacao.Negocios;

/// <summary>Consulta somente-leitura de negócios para uso em rotas anônimas (resolução por subdomínio).</summary>
public interface IConsultaNegocioPublico
{
    /// <summary>Busca um negócio ativo pelo slug. Retorna <c>null</c> se não existir ou estiver inativo.</summary>
    Task<NegocioResumo?> ObterPorSlugAsync(Slug slug, CancellationToken cancellationToken = default);
}
