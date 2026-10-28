using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Negocios;

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

    /// <param name="agenteIa">Filtra pelo serviço de IA do WhatsApp (contratado ou não); nulo = todos.</param>
    Task<IReadOnlyList<NegocioNaPlataforma>> ListarNegociosAsync(EstadoAssinatura? estado, bool? agenteIa = null, CancellationToken cancellationToken = default);

    Task<DetalheNegocioNaPlataforma?> ObterNegocioAsync(Guid negocioId, CancellationToken cancellationToken = default);

    Task<bool> RegistrarPagamentoAsync(string autor, Guid negocioId, PagamentoManual dados, CancellationToken cancellationToken = default);

    Task<bool> EstenderTesteAsync(string autor, Guid negocioId, int dias, CancellationToken cancellationToken = default);

    Task<bool> TrocarPlanoAsync(string autor, Guid negocioId, Guid planoId, Periodicidade periodicidade, CancellationToken cancellationToken = default);

    Task<bool> SuspenderAsync(string autor, Guid negocioId, string motivo, CancellationToken cancellationToken = default);

    Task<bool> ReativarAsync(string autor, Guid negocioId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Liga ou desliga o serviço de IA do WhatsApp do negócio (origem Manual) com auditoria. Repetir o estado atual devolve o estado sem
    /// nova linha de auditoria. Nulo = negócio inexistente.
    /// </summary>
    Task<ServicoAgenteIaNegocio?> DefinirAgenteIaAsync(string autor, Guid negocioId, bool contratado, string? motivo, CancellationToken cancellationToken = default);

    /// <summary>Até 100 alterações, a mais recente primeiro. Nulo = negócio inexistente.</summary>
    Task<IReadOnlyList<HistoricoAgenteIa>?> ListarHistoricoAgenteIaAsync(Guid negocioId, CancellationToken cancellationToken = default);
}

public sealed record SessaoPlataforma(string AccessToken, DateTimeOffset ExpiraEm, string Nome);

public sealed record NegocioNaPlataforma(
    Guid Id, string Nome, string Slug, string Tipo, string? Plano, EstadoAssinatura? Estado,
    DateTimeOffset? FimTeste, DateTimeOffset? ProximoVencimento, DateTimeOffset CadastradoEm,
    bool AgenteIaContratado = false, OrigemAgenteIa? AgenteIaOrigem = null, bool WhatsAppConectado = false);

public sealed record DetalheNegocioNaPlataforma(
    NegocioNaPlataforma Negocio, Guid? PlanoId, Periodicidade? Periodicidade, decimal? PrecoMensalTravado,
    decimal? ValorDoPeriodo, DateTimeOffset? CarenciaAte, int ProfissionaisAtivos,
    IReadOnlyList<HistoricoAssinaturaDto> Historico, IReadOnlyList<CobrancaAssinaturaDto> Cobrancas,
    ServicoAgenteIaNegocio? AgenteIa = null);

/// <summary>Serviço de IA do WhatsApp visto pelo administrador da plataforma. <c>Origem</c> é nula quando desligado.</summary>
public sealed record ServicoAgenteIaNegocio(bool Contratado, OrigemAgenteIa? Origem, DateTimeOffset? AlteradoEm, string? AlteradoPor);

/// <summary><c>Acao</c>: <c>LigarAgenteIa</c> ou <c>DesligarAgenteIa</c>. <c>Detalhes</c>: "de desligado para ligado; origem Manual; motivo: ...".</summary>
public sealed record HistoricoAgenteIa(DateTimeOffset Em, string Autor, string Acao, string? Detalhes);

public sealed record HistoricoAssinaturaDto(DateTimeOffset Em, EstadoAssinatura? EstadoAnterior, EstadoAssinatura EstadoNovo, string Autor, string Motivo);

/// <summary><c>EstornadaEm</c>: estornada ou contestada no gateway — o período dela não vale mais.</summary>
public sealed record CobrancaAssinaturaDto(
    DateTimeOffset PagoEm, decimal Valor, FormaCobranca Forma, DateTimeOffset PeriodoInicio, DateTimeOffset PeriodoFim, OrigemCobranca Origem,
    DateTimeOffset? EstornadaEm = null);

public sealed record PagamentoManual(decimal Valor, FormaCobranca Forma, DateTimeOffset PagoEm, DateTimeOffset PeriodoInicio, DateTimeOffset PeriodoFim);
