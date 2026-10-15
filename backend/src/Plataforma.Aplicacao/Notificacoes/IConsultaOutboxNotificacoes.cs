namespace Plataforma.Aplicacao.Notificacoes;

/// <summary>
/// Acompanhamento da outbox de notificações pela administração da plataforma (B7.4-B): fila, falhas e mensagens de resultado
/// desconhecido de TODOS os negócios. Nunca devolve dado pessoal — só ids, tipo, canal, estado, contagens e códigos técnicos.
/// </summary>
public interface IConsultaOutboxNotificacoes
{
    Task<ResumoOutbox> ObterResumoAsync(CancellationToken cancellationToken = default);

    Task<PaginaOutbox> ListarAsync(FiltroOutbox filtro, CancellationToken cancellationToken = default);
}

/// <param name="Pendentes">Esperando o despacho (inclui as que aguardam nova tentativa).</param>
/// <param name="Enviando">Reivindicadas por um despachante agora (lease vigente ou vencido, ainda não recuperado).</param>
/// <param name="Falhas">Falha definitiva (recusa, tentativas esgotadas ou prazo de 24 h vencido) ainda dentro da retenção.</param>
/// <param name="Desconhecidas">WhatsApp que pode ter sido aceito mas sem confirmação: nunca reenviado, para acompanhamento.</param>
/// <param name="IdadeDaPendenciaMaisAntigaSegundos">Há quanto tempo a mensagem pendente mais antiga espera para sair (nulo sem pendências).</param>
/// <param name="FalhasRecentes">As últimas falhas definitivas e desconhecidas, da mais nova para a mais antiga.</param>
public sealed record ResumoOutbox(
    int Pendentes, int Enviando, int Falhas, int Desconhecidas, double? IdadeDaPendenciaMaisAntigaSegundos,
    DateTimeOffset? PendenciaMaisAntigaDesde, DateTimeOffset GeradoEm, IReadOnlyList<ItemOutbox> FalhasRecentes);

/// <summary>Uma mensagem da outbox, sem destinatário, sem conteúdo e sem resposta do provedor.</summary>
public sealed record ItemOutbox(
    Guid Id, Guid NegocioId, string Tipo, string Canal, string Status, int Tentativas, DateTimeOffset CriadaEm, DateTimeOffset? AtualizadaEm,
    DateTimeOffset? ProximaTentativaEm, DateTimeOffset ExpiraEm, DateTimeOffset? EnviadaEm, DateTimeOffset? CanceladaEm,
    string? UltimoErroCodigo, Guid? AgendamentoId);

/// <summary>
/// <paramref name="De"/>/<paramref name="Ate"/> filtram pela última atualização (quando o item chegou ao estado atual). Página a partir de 1,
/// até 200 itens; ordem: mais recentemente atualizados primeiro.
/// </summary>
public sealed record FiltroOutbox(
    string? Status = null, string? Tipo = null, string? Canal = null, Guid? NegocioId = null,
    DateTimeOffset? De = null, DateTimeOffset? Ate = null, int Pagina = 1, int Tamanho = 50);

public sealed record PaginaOutbox(IReadOnlyList<ItemOutbox> Itens, int Pagina, int Tamanho, int Total);
