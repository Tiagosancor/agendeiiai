namespace Plataforma.Aplicacao.Clientes;

public interface IGerenciadorClientes
{
    Task<IReadOnlyList<ClienteResumo>> ListarAsync(CancellationToken cancellationToken = default);

    Task<ClienteResumo?> ObterAsync(Guid clienteId, CancellationToken cancellationToken = default);

    /// <summary>Lança <see cref="TelefoneJaCadastradoException"/> se o telefone já existir neste negócio (seção 7).</summary>
    Task<Guid> CriarAsync(CriarCliente dados, CancellationToken cancellationToken = default);

    Task<bool> AtualizarAsync(Guid clienteId, AtualizarCliente dados, CancellationToken cancellationToken = default);

    /// <summary>Exportação de dados sob demanda (LGPD, seção 8.4) — cliente + histórico de agendamentos.</summary>
    Task<ExportacaoCliente?> ExportarAsync(Guid clienteId, CancellationToken cancellationToken = default);

    /// <summary>Exclusão sob demanda (LGPD, seção 8.4) — anonimiza (ver <c>Cliente.Anonimizar</c>), nunca apaga a linha.</summary>
    Task<bool> ExcluirAsync(Guid clienteId, CancellationToken cancellationToken = default);
}

public sealed record ClienteResumo(Guid Id, string Nome, string Telefone, string? Email, string? Observacoes, bool Excluido);

public sealed record CriarCliente(string Nome, string Telefone, string? Email = null, string? Observacoes = null);

public sealed record AtualizarCliente(string Nome, string? Email, string? Observacoes);

public sealed record ExportacaoCliente(
    Guid Id, string Nome, string Telefone, string? Email, string? Observacoes, string Origem, DateTimeOffset CriadoEm,
    IReadOnlyList<ExportacaoAgendamento> Agendamentos);

public sealed record ExportacaoAgendamento(
    DateTimeOffset Inicio, DateTimeOffset Fim, string Status, IReadOnlyList<string> Servicos, decimal Total, string? Observacoes);

public sealed class TelefoneJaCadastradoException(string telefone)
    : Exception($"Já existe um cliente com o telefone '{telefone}' neste negócio.");
