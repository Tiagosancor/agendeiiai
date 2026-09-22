namespace Plataforma.Aplicacao.Profissionais;

/// <summary>Horário de trabalho por dia da semana (seção 7) — um profissional pode ter mais de um intervalo por dia (o buraco entre eles é o almoço).</summary>
public interface IGerenciadorHorariosTrabalho
{
    Task<IReadOnlyList<IntervaloTrabalho>> ListarAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    /// <summary>Substitui todos os intervalos do profissional pelos informados.</summary>
    Task DefinirAsync(Guid profissionalId, IReadOnlyList<IntervaloTrabalho> intervalos, CancellationToken cancellationToken = default);
}

public sealed record IntervaloTrabalho(int DiaSemana, TimeOnly Inicio, TimeOnly Fim);
