namespace Plataforma.Aplicacao.WhatsAppAgente;

public sealed record ContextoRespostaAgente(
    Guid NegocioId, Guid InstanciaId, Guid ConversaId, string TelefoneCliente, string TextoRecebido,
    DateTimeOffset? LimiteHistorico = null, Guid? RodadaId = null);
// LimiteHistorico: instante da última mensagem do lote que esta rodada vai marcar como processado. O histórico enviado à IA
// só inclui mensagens até ele (inclusive); o que chegar depois fica pendente para a rodada seguinte, sem ser respondido duas vezes.
// RodadaId: id da linha de diagnóstico (rodadas_agente) que o processador gravará ao fim; preenchido só com Ai__RegistrarRodadas ligado.

/// <summary>
/// O que o agente decidiu para a rodada. <c>Texto</c> nulo = não responder. <c>PendenciaPreparadaId</c> preenchido = a ferramenta
/// de preparar agendamento reservou o horário nesta rodada: o texto do modelo (se houver) é DESCARTADO e quem envia o resumo é o
/// servidor. Só o processador lê esse id; ele nunca vai ao modelo.
/// </summary>
public sealed record RespostaAgente(string? Texto, Guid? PendenciaPreparadaId = null, TelemetriaRodadaAgente? Telemetria = null);

/// <summary>
/// O que a rodada fez, só em números e códigos fechados (nunca texto de mensagem, argumento de ferramenta, telefone ou token), para
/// o processador gravar em <c>rodadas_agente</c>. Nulo quando o registro está desligado ou o provedor não é a IA.
/// </summary>
public sealed record TelemetriaRodadaAgente(
    int Chamadas, int TokensEntrada, int TokensSaida, string? Modelo, string? MotivoFim, IReadOnlyList<string> Ferramentas,
    IReadOnlyList<string> ErrosFerramenta, string? FalhaProvedor, bool Fallback, string? PromptVersao);

/// <summary>
/// Ponto de troca entre o eco temporário da Fase 1 e o orquestrador de IA da Fase 2. Devolve a resposta a
/// enviar ao cliente (texto <c>null</c> para não responder).
/// </summary>
public interface IRespostaAgente
{
    Task<RespostaAgente> ResponderAsync(ContextoRespostaAgente contexto, CancellationToken cancellationToken = default);
}
