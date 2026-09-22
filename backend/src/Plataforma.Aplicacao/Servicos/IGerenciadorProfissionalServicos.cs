namespace Plataforma.Aplicacao.Servicos;

/// <summary>Serviços que cada profissional executa, com preço/duração próprios opcionais (seção 7).</summary>
public interface IGerenciadorProfissionalServicos
{
    Task<IReadOnlyList<ProfissionalServicoResumo>> ListarAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    Task VincularAsync(
        Guid profissionalId, Guid servicoId, decimal? precoPersonalizado, int? duracaoPersonalizadaMinutos,
        CancellationToken cancellationToken = default);

    Task<bool> DesvincularAsync(Guid profissionalId, Guid servicoId, CancellationToken cancellationToken = default);
}

public sealed record ProfissionalServicoResumo(Guid ServicoId, string Nome, decimal Preco, int DuracaoMinutos);
