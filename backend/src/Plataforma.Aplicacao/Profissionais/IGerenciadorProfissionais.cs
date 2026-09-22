namespace Plataforma.Aplicacao.Profissionais;

public interface IGerenciadorProfissionais
{
    Task<IReadOnlyList<ProfissionalResumo>> ListarAsync(CancellationToken cancellationToken = default);

    Task<ProfissionalDetalhe?> ObterAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    Task<Guid> CriarAsync(CriarProfissional dados, CancellationToken cancellationToken = default);

    Task<bool> AtualizarDadosAsync(Guid profissionalId, AtualizarProfissional dados, CancellationToken cancellationToken = default);

    Task<bool> DesativarAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    Task<bool> AtivarAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    Task<string?> RevelarCpfAsync(Guid profissionalId, CancellationToken cancellationToken = default);
}

public sealed record ProfissionalResumo(Guid Id, string Nome, bool Ativo, string? FotoUrl);

public sealed record ProfissionalDetalhe(
    Guid Id, string Nome, string? Telefone, string? Email, bool Ativo, string? FotoUrl, string? CpfMascarado);

public sealed record CriarProfissional(string Nome, string? Telefone = null, string? Email = null, string? Cpf = null);

public sealed record AtualizarProfissional(string Nome, string? Telefone, string? Email);
