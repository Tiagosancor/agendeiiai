using Plataforma.Dominio.Assinaturas;

namespace Plataforma.Aplicacao.Administracao;

/// <summary>
/// Administração da plataforma (seção 7/8.6.7) — a única parte do sistema que enxerga vários
/// negócios. Só dados de assinatura, nunca de clientes finais. Toda ação vai para o log de
/// auditoria, e toda mudança de assinatura passa pelo <c>ServicoAssinatura</c>.
/// Métodos de ação devolvem falso quando o negócio não existe.
/// </summary>
public interface IAdministracaoPlataforma
{
    Task<SessaoPlataforma?> EntrarAsync(string email, string senha, CancellationToken cancellationToken = default);

    /// <summary>Usado só pelo comando de linha. Devolve verdadeiro se criou, falso se já existia (e redefiniu a senha).</summary>
    Task<bool> CriarOuRedefinirAdministradorAsync(string email, string nome, string senha, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NegocioNaPlataforma>> ListarNegociosAsync(EstadoAssinatura? estado, CancellationToken cancellationToken = default);

    Task<DetalheNegocioNaPlataforma?> ObterNegocioAsync(Guid negocioId, CancellationToken cancellationToken = default);

    Task<bool> RegistrarPagamentoAsync(string autor, Guid negocioId, PagamentoManual dados, CancellationToken cancellationToken = default);

    Task<bool> EstenderTesteAsync(string autor, Guid negocioId, int dias, CancellationToken cancellationToken = default);

    Task<bool> TrocarPlanoAsync(string autor, Guid negocioId, Guid planoId, Periodicidade periodicidade, CancellationToken cancellationToken = default);

    Task<bool> SuspenderAsync(string autor, Guid negocioId, string motivo, CancellationToken cancellationToken = default);

    Task<bool> ReativarAsync(string autor, Guid negocioId, CancellationToken cancellationToken = default);
}

public sealed record SessaoPlataforma(string AccessToken, DateTimeOffset ExpiraEm, string Nome);

public sealed record NegocioNaPlataforma(
    Guid Id, string Nome, string Slug, string Tipo, string? Plano, EstadoAssinatura? Estado,
    DateTimeOffset? FimTeste, DateTimeOffset? ProximoVencimento, DateTimeOffset CadastradoEm);

public sealed record DetalheNegocioNaPlataforma(
    NegocioNaPlataforma Negocio, Guid? PlanoId, Periodicidade? Periodicidade, decimal? PrecoMensalTravado,
    decimal? ValorDoPeriodo, DateTimeOffset? CarenciaAte, int ProfissionaisAtivos,
    IReadOnlyList<HistoricoAssinaturaDto> Historico, IReadOnlyList<CobrancaAssinaturaDto> Cobrancas);

public sealed record HistoricoAssinaturaDto(DateTimeOffset Em, EstadoAssinatura? EstadoAnterior, EstadoAssinatura EstadoNovo, string Autor, string Motivo);

public sealed record CobrancaAssinaturaDto(
    DateTimeOffset PagoEm, decimal Valor, FormaCobranca Forma, DateTimeOffset PeriodoInicio, DateTimeOffset PeriodoFim, OrigemCobranca Origem);

public sealed record PagamentoManual(decimal Valor, FormaCobranca Forma, DateTimeOffset PagoEm, DateTimeOffset PeriodoInicio, DateTimeOffset PeriodoFim);
