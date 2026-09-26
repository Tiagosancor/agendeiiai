using Plataforma.Aplicacao.Cadastros;

namespace Plataforma.Aplicacao.Servicos;

public interface IGerenciadorServicos
{
    Task<IReadOnlyList<ServicoResumo>> ListarAsync(CancellationToken cancellationToken = default);

    Task<Guid> CriarAsync(CriarServico dados, CancellationToken cancellationToken = default);

    Task<bool> AtualizarAsync(Guid servicoId, AtualizarServico dados, CancellationToken cancellationToken = default);

    Task<bool> DesativarAsync(Guid servicoId, CancellationToken cancellationToken = default);

    Task<bool> AtivarAsync(Guid servicoId, CancellationToken cancellationToken = default);

    Task<PreviaExclusao?> ObterPreviaExclusaoAsync(Guid servicoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sai da oferta na hora; agendamentos já marcados continuam válidos com o nome, preço e
    /// duração gravados no momento (seção 7) — por isso não bloqueia com agendamento futuro.
    /// </summary>
    Task<ResultadoExclusao> ExcluirAsync(Guid servicoId, CancellationToken cancellationToken = default);
}

public sealed record ServicoResumo(
    Guid Id, Guid CategoriaId, string Nome, decimal Preco, int DuracaoMinutos, bool Popular, bool Ativo);

public sealed record CriarServico(Guid CategoriaId, string Nome, decimal Preco, int DuracaoMinutos, bool Popular = false);

public sealed record AtualizarServico(Guid CategoriaId, string Nome, decimal Preco, int DuracaoMinutos, bool Popular);
