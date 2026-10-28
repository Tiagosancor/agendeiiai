using System.Text.Json;

namespace Plataforma.Aplicacao.WhatsAppAgente;

/// <summary>
/// Quem a conversa é, montado SÓ pelo servidor. O modelo nunca informa nem decide o negócio, a instância ou a conversa
/// (nem o telefone, que sequer faz parte deste contexto).
/// </summary>
public sealed record ContextoConversaAgente(Guid NegocioId, Guid InstanciaId, Guid ConversaId);

/// <summary>
/// Ferramenta que o modelo pode pedir. Na Fase 2 todas são SOMENTE LEITURA de dado público do próprio negócio. O retorno é
/// sempre um JSON de lista fechada de campos; erro de validação volta como <c>{ "erro", "mensagem" }</c>, nunca exceção.
/// </summary>
public interface IFerramentaAgente
{
    string Nome { get; }

    string Descricao { get; }

    /// <summary>JSON Schema dos parâmetros (<c>additionalProperties: false</c>).</summary>
    JsonElement Esquema { get; }

    Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default);
}

/// <summary>O que o servidor faz depois que a rodada termina, a pedido de uma ferramenta (o texto do modelo é descartado).</summary>
public enum TipoAcaoServidor
{
    /// <summary>O agente chamou um atendente: a conversa já está em Humano; o servidor envia o texto fixo de transferência.</summary>
    Transferencia = 1,
}

/// <summary>
/// Sinal da ferramenta ao servidor. <c>Motivo</c> é sempre um código fechado (nunca texto do modelo); <c>AvisarAtendente</c> diz que
/// este chamado passou nos limites de e-mail e o dono deve ser avisado.
/// </summary>
public sealed record AcaoServidor(TipoAcaoServidor Tipo, Guid Id, string? Motivo = null, bool AvisarAtendente = false);

/// <summary>Resultado de uma ferramenta mais o sinal para o servidor. O sinal nunca vai ao modelo.</summary>
public sealed record ResultadoFerramentaAgente(JsonElement Conteudo, Guid? PendenciaPreparadaId = null, AcaoServidor? Acao = null);

/// <summary>
/// Ferramenta que ESCREVE (Fase 3: só prepara um agendamento, nunca o confirma). Só é oferecida ao modelo e só executa quando o
/// agendamento pelo agente está ligado para o negócio. Preparar com sucesso devolve a pendência: a rodada termina e o servidor
/// envia o resumo.
/// </summary>
public interface IFerramentaDeEscritaAgente : IFerramentaAgente
{
    Task<ResultadoFerramentaAgente> ExecutarComSinalAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ferramenta que passa a conversa para o atendimento humano (<c>chamar_atendente</c>). Escreve (muda o estado da conversa), mas não
/// depende do agendamento pelo agente: só do serviço de IA e da flag <c>Ai__Humano__FerramentaAtiva</c>.
/// </summary>
public interface IFerramentaDeAtendimentoHumano : IFerramentaDeEscritaAgente
{
}

/// <summary>Executa uma ferramenta pelo nome, validando contexto e argumentos. Nunca lança por entrada do modelo.</summary>
public interface IExecutorFerramentasAgente
{
    IReadOnlyList<IFerramentaAgente> Ferramentas { get; }

    /// <summary>O catálogo oferecido ao modelo neste negócio: as de leitura sempre; as de escrita só com o agendamento ligado.</summary>
    IReadOnlyList<IFerramentaAgente> FerramentasPara(Guid negocioId);

    Task<ResultadoFerramentaAgente> ExecutarComSinalAsync(ContextoConversaAgente contexto, string nome, JsonElement argumentos, CancellationToken cancellationToken = default);

    Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, string nome, JsonElement argumentos, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fila de rodadas do agente: só o <c>ConversaId</c> trafega (nada de conteúdo fora do banco). O webhook enfileira e responde
/// 200 sem esperar a IA. Devolve falso se a fila está cheia (a varredura de recuperação pega depois).
/// </summary>
public interface IFilaRespostaAgente
{
    Task<bool> EnfileirarAsync(Guid conversaId, CancellationToken cancellationToken = default);
}

/// <summary>Uma rodada completa do agente numa conversa (lease, limites, IA, envio). Escopo de DI novo por chamada.</summary>
public interface IProcessadorRodadaAgente
{
    Task ProcessarAsync(Guid conversaId, CancellationToken cancellationToken = default);
}
