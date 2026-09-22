namespace Plataforma.Aplicacao.Cupons;

/// <summary>CRUD de cupons no painel (seção 7). A aplicação/revalidação do cupom em um agendamento é responsabilidade do <c>IServicoAgendamentos</c> (assistente público, Sprint 3).</summary>
public interface IGerenciadorCupons
{
    Task<IReadOnlyList<CupomResumo>> ListarAsync(CancellationToken cancellationToken = default);

    Task<Guid> CriarAsync(CriarCupom dados, CancellationToken cancellationToken = default);

    Task<bool> AtualizarAsync(Guid cupomId, AtualizarCupom dados, CancellationToken cancellationToken = default);

    Task<bool> DesativarAsync(Guid cupomId, CancellationToken cancellationToken = default);

    Task<bool> AtivarAsync(Guid cupomId, CancellationToken cancellationToken = default);
}

public sealed record CupomResumo(
    Guid Id, string Codigo, string Tipo, decimal Valor, DateTimeOffset? ValidoAte,
    int? LimiteUsos, int UsosAtuais, bool Ativo, IReadOnlyCollection<Guid> ServicoIdsEscopo);

public sealed record CriarCupom(
    string Codigo, string Tipo, decimal Valor, DateTimeOffset? ValidoAte = null,
    int? LimiteUsos = null, IReadOnlyCollection<Guid>? ServicoIdsEscopo = null);

public sealed record AtualizarCupom(
    DateTimeOffset? ValidoAte, int? LimiteUsos, IReadOnlyCollection<Guid>? ServicoIdsEscopo);
