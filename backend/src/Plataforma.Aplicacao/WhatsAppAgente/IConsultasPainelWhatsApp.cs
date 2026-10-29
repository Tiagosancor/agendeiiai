namespace Plataforma.Aplicacao.WhatsAppAgente;

/// <summary>Filtro da lista de conversas do painel.</summary>
public enum FiltroConversasWhatsApp
{
    Todas = 0,

    /// <summary>Em atendimento humano que ninguém assumiu ainda (o agente chamou um atendente).</summary>
    Aguardando = 1,

    /// <summary>Todas as que estão em atendimento humano (assumidas ou não).</summary>
    Humano = 2,
}

/// <param name="Telefone">E.164 completo: quem tem a permissão precisa achar a conversa no celular.</param>
/// <param name="ClienteNome">Do cadastro, se houver cliente com esse telefone (nas duas formas do 9º dígito).</param>
/// <param name="Status">"Agente" ou "Humano".</param>
/// <param name="HumanoMotivo">"Painel" ou "PedidoDoAgente"; nulo fora do atendimento humano.</param>
/// <param name="MensagensDesdeHumano">Mensagens do cliente recebidas depois que a conversa foi para o humano (0 fora do modo Humano).</param>
/// <param name="AguardandoAtendimento">Em Humano e ninguém assumiu ainda.</param>
public sealed record ConversaWhatsAppResumo(
    Guid Id, string Telefone, string? ClienteNome, string Status, DateTimeOffset? HumanoDesde, string? HumanoMotivo,
    DateTimeOffset? HumanoAte, DateTimeOffset UltimaMensagemEm, int MensagensDesdeHumano, bool AguardandoAtendimento);

public sealed record PaginaConversasWhatsApp(IReadOnlyList<ConversaWhatsAppResumo> Itens, int Total, int Pagina, int TamanhoPagina);

/// <param name="Papel">"Cliente", "Assistente" ou "Sistema" (texto fixo do servidor). Nunca ferramenta nem nota de contexto.</param>
public sealed record MensagemConversaWhatsApp(Guid Id, string Papel, string Texto, DateTimeOffset Em);

/// <summary>Os mesmos campos de <see cref="ConversaWhatsAppResumo"/> mais as mensagens (ordem cronológica, até 50).</summary>
public sealed record DetalheConversaWhatsApp(
    Guid Id, string Telefone, string? ClienteNome, string Status, DateTimeOffset? HumanoDesde, string? HumanoMotivo,
    DateTimeOffset? HumanoAte, DateTimeOffset UltimaMensagemEm, int MensagensDesdeHumano, bool AguardandoAtendimento,
    IReadOnlyList<MensagemConversaWhatsApp> Mensagens, bool HaMaisAntigas);

/// <param name="AguardandoAtendimento">Conversas em Humano que ninguém assumiu (pedidos de atendente pendentes).</param>
/// <param name="EmAtendimentoHumano">Todas as conversas em Humano.</param>
public sealed record ResumoConversasWhatsApp(int AguardandoAtendimento, int EmAtendimentoHumano);

/// <summary>
/// Leitura das conversas do agente para o painel (Fase 4, Fatia 4). Sempre no negócio do contexto; conversa de outro negócio,
/// inexistente ou anonimizada é "não encontrada". Nenhum texto de conversa em log.
/// </summary>
public interface IConsultaConversasWhatsApp
{
    /// <summary>Lista paginada, mais recente primeiro. NÃO traz texto de mensagem.</summary>
    Task<PaginaConversasWhatsApp> ListarAsync(FiltroConversasWhatsApp filtro, int pagina, CancellationToken cancellationToken = default);

    /// <summary>Até 50 mensagens em ordem cronológica; <paramref name="antesDe"/> pagina para trás. Nulo se não existe neste negócio.</summary>
    Task<DetalheConversaWhatsApp?> ObterAsync(Guid conversaId, DateTimeOffset? antesDe, CancellationToken cancellationToken = default);

    Task<ResumoConversasWhatsApp> ResumoAsync(CancellationToken cancellationToken = default);
}

/// <param name="Dia">"AAAA-MM-DD" (UTC).</param>
public sealed record UsoIaDia(string Dia, int Chamadas, long Tokens);

public sealed record UsoIaMes(
    string Mes, int Chamadas, int ChamadasComFalha, long TokensEntrada, long TokensSaida, long CotaMensalTokens, decimal PercentualCota,
    int ChamadasHoje, int LimiteChamadasPorDia, int ConversasAtendidas, int RespostasFallback, int AgendamentosConfirmados,
    int Cancelamentos, int Remarcacoes, int TransferenciasHumano, IReadOnlyList<UsoIaDia> PorDia);

/// <summary>Uso de IA do negócio no mês UTC (o mesmo da cota). Só números.</summary>
public interface IConsultaUsoIaWhatsApp
{
    Task<UsoIaMes> ObterAsync(int ano, int mes, CancellationToken cancellationToken = default);
}
