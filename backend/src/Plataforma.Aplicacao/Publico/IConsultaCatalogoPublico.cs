namespace Plataforma.Aplicacao.Publico;

/// <summary>
/// Catálogo público do negócio (seção 6.1.3/6.1.4) — só o que já é público por natureza
/// (equipe, serviços e preços do site); nada de dado pessoal de cliente passa por aqui
/// (seção 8.1.3).
/// </summary>
public interface IConsultaCatalogoPublico
{
    Task<IReadOnlyList<CategoriaComServicosPublicos>> ListarServicosAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProfissionalPublico>> ListarProfissionaisAsync(CancellationToken cancellationToken = default);
}

public sealed record CategoriaComServicosPublicos(Guid CategoriaId, string Nome, IReadOnlyList<ServicoPublico> Servicos);

public sealed record ServicoPublico(Guid Id, string Nome, decimal Preco, int DuracaoMinutos, bool Popular);

public sealed record ProfissionalPublico(Guid Id, string Nome, string? FotoUrl, string? Funcao, IReadOnlyList<Guid> ServicoIds);
