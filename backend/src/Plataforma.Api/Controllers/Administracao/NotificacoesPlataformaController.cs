using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Notificacoes;

namespace Plataforma.Api.Controllers.Administracao;

/// <summary>
/// Acompanhamento da outbox de notificações (B7.4-B), só para a administração da plataforma: fila, falhas e mensagens de resultado
/// desconhecido de todos os negócios, sem dado pessoal. Somente leitura.
/// </summary>
[ApiController]
[Route("plataforma/notificacoes")]
[Authorize(Policy = ClaimsPlataforma.PoliticaAdministradorPlataforma)]
public sealed class NotificacoesPlataformaController : ControllerBase
{
    private readonly IConsultaOutboxNotificacoes _consulta;

    public NotificacoesPlataformaController(IConsultaOutboxNotificacoes consulta) => _consulta = consulta;

    /// <summary>Contagens por estado, idade da pendência mais antiga e as últimas falhas.</summary>
    [HttpGet("resumo")]
    public async Task<ActionResult<ResumoOutbox>> Resumo(CancellationToken cancellationToken) =>
        Ok(await _consulta.ObterResumoAsync(cancellationToken));

    /// <summary>
    /// Lista paginada. <c>status</c> (Pendente, Enviando, Enviada, Falhou, Cancelada, Desconhecida), <c>tipo</c>, <c>canal</c>
    /// (Email, WhatsApp), <c>negocioId</c>, <c>de</c>/<c>ate</c> (última atualização), <c>pagina</c> (de 1) e <c>tamanho</c> (até 200).
    /// </summary>
    [HttpGet("outbox")]
    public async Task<ActionResult<PaginaOutbox>> Listar(
        [FromQuery] string? status, [FromQuery] string? tipo, [FromQuery] string? canal, [FromQuery] Guid? negocioId,
        [FromQuery] DateTimeOffset? de, [FromQuery] DateTimeOffset? ate, [FromQuery] int pagina = 1, [FromQuery] int tamanho = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await _consulta.ListarAsync(new FiltroOutbox(status, tipo, canal, negocioId, de, ate, pagina, tamanho), cancellationToken));
}
