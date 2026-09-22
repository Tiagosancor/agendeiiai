namespace Plataforma.Aplicacao.Profissionais;

/// <summary>Folgas e bloqueios por período — horas ou dias (seção 7). Enquanto bloqueado, o horário não é oferecido.</summary>
public interface IGerenciadorBloqueios
{
    Task<IReadOnlyList<BloqueioResumo>> ListarAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    Task<Guid> CriarAsync(Guid profissionalId, DateTimeOffset inicioUtc, DateTimeOffset fimUtc, string? motivo, CancellationToken cancellationToken = default);

    Task<bool> RemoverAsync(Guid bloqueioId, CancellationToken cancellationToken = default);
}

public sealed record BloqueioResumo(Guid Id, Guid ProfissionalId, DateTimeOffset InicioUtc, DateTimeOffset FimUtc, string? Motivo);
