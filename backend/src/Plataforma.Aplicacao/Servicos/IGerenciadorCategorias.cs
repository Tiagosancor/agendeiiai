namespace Plataforma.Aplicacao.Servicos;

public interface IGerenciadorCategorias
{
    Task<IReadOnlyList<CategoriaResumo>> ListarAsync(CancellationToken cancellationToken = default);

    Task<Guid> CriarAsync(string nome, CancellationToken cancellationToken = default);

    Task<bool> RenomearAsync(Guid categoriaId, string nome, CancellationToken cancellationToken = default);

    Task<bool> DesativarAsync(Guid categoriaId, CancellationToken cancellationToken = default);

    Task<bool> AtivarAsync(Guid categoriaId, CancellationToken cancellationToken = default);
}

public sealed record CategoriaResumo(Guid Id, string Nome, bool Ativa);
