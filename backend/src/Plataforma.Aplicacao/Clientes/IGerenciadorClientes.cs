namespace Plataforma.Aplicacao.Clientes;

public interface IGerenciadorClientes
{
    Task<IReadOnlyList<ClienteResumo>> ListarAsync(CancellationToken cancellationToken = default);

    Task<ClienteResumo?> ObterAsync(Guid clienteId, CancellationToken cancellationToken = default);

    /// <summary>Lança <see cref="TelefoneJaCadastradoException"/> se o telefone já existir neste negócio (seção 7).</summary>
    Task<Guid> CriarAsync(CriarCliente dados, CancellationToken cancellationToken = default);

    Task<bool> AtualizarAsync(Guid clienteId, AtualizarCliente dados, CancellationToken cancellationToken = default);
}

public sealed record ClienteResumo(Guid Id, string Nome, string Telefone, string? Email, string? Observacoes);

public sealed record CriarCliente(string Nome, string Telefone, string? Email = null, string? Observacoes = null);

public sealed record AtualizarCliente(string Nome, string? Email, string? Observacoes);

public sealed class TelefoneJaCadastradoException(string telefone)
    : Exception($"Já existe um cliente com o telefone '{telefone}' neste negócio.");
