using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.WhatsAppAgente;

/// <summary>Como uma rodada do agente terminou (diagnóstico persistente, Fase 4).</summary>
public enum DesfechoRodadaAgente
{
    /// <summary>Texto do modelo (ou do eco) enviado ao cliente.</summary>
    Respondida = 1,

    /// <summary>O modelo falhou ou não produziu texto aproveitável: foi o texto fixo com o link público.</summary>
    Fallback = 2,

    /// <summary>De propósito, nada foi respondido (ver <see cref="MotivoSilencioRodada"/>) ou o agente não tinha o que dizer.</summary>
    Silencio = 3,

    LimiteTelefone = 4,

    LimiteCusto = 5,

    /// <summary>O modelo preparou um agendamento e o servidor enviou o resumo.</summary>
    ResumoEnviado = 6,

    /// <summary>Resposta a um resumo ("sim"/"não") tratada pelo servidor, sem chamar a IA.</summary>
    Confirmacao = 7,

    GestaoEfetivada = 8,

    Transferida = 9,

    /// <summary>A posse da conversa se perdeu durante a rodada: nada foi enviado nem marcado.</summary>
    LeasePerdido = 10,

    /// <summary>Envio comprovadamente recusado (Evolution fora do ar) ou sem prazo de lease.</summary>
    EnvioFalhou = 11,

    /// <summary>Envio com resultado desconhecido (timeout).</summary>
    EnvioAmbiguo = 12,

    /// <summary>Exceção inesperada na rodada.</summary>
    Erro = 13,

    /// <summary>Reservado para a fatia do serviço premium do agente (ainda não gravado): agente indisponível para o negócio.</summary>
    Indisponivel = 14,
}

public enum MotivoSilencioRodada
{
    Humano = 1,
    AgenteDesligado = 2,
    Assinatura = 3,
    ForaDoPiloto = 4,

    /// <summary>Mensagem velha demais (por exemplo, pendente antes de um deploy).</summary>
    Antiga = 5,

    /// <summary>Reservado para a fatia do serviço premium do agente (ainda não gravado).</summary>
    NaoContratado = 6,
}

/// <summary>
/// Uma linha por rodada do agente numa conversa (<c>rodadas_agente</c>). Existe para o diagnóstico sobreviver aos poucos horas de log
/// da hospedagem: guarda SÓ números, códigos fechados e nomes do catálogo de ferramentas — nunca telefone, nome, texto de mensagem,
/// argumento de ferramenta, token ou segredo.
/// </summary>
public class RodadaAgente : EntidadeBase, IEntidadeDoNegocio
{
    public const int TamanhoMaximoLista = 200;
    public const int TamanhoMaximoCodigo = 40;

    public Guid NegocioId { get; private set; }

    public Guid ConversaId { get; private set; }

    public DateTimeOffset IniciadaEm { get; private set; }

    public int DuracaoMs { get; private set; }

    public DesfechoRodadaAgente Desfecho { get; private set; }

    public MotivoSilencioRodada? MotivoSilencio { get; private set; }

    /// <summary>Chamadas ao provedor de IA feitas na rodada (com falha ou não).</summary>
    public int Chamadas { get; private set; }

    public int TokensEntrada { get; private set; }

    public int TokensSaida { get; private set; }

    public string? Modelo { get; private set; }

    /// <summary>Último motivo de término do provedor (ex.: STOP, MAX_TOKENS) ou marcador nosso (LACO_FERRAMENTAS, ORCAMENTO_TEMPO, LIMITE_CUSTO).</summary>
    public string? MotivoFim { get; private set; }

    /// <summary>Nomes do catálogo de ferramentas chamadas, em ordem, separados por vírgula (nome fora do catálogo vira <c>ferramenta_desconhecida</c>).</summary>
    public string? Ferramentas { get; private set; }

    /// <summary>Códigos de erro devolvidos pelas ferramentas (ex.: <c>horario_indisponivel</c>), separados por vírgula.</summary>
    public string? ErrosFerramenta { get; private set; }

    /// <summary>Falha do provedor: tipo e status HTTP (ex.: <c>http:503</c>, <c>timeout</c>).</summary>
    public string? FalhaProvedor { get; private set; }

    public string? PromptVersao { get; private set; }

    protected RodadaAgente()
    {
    }

    public RodadaAgente(
        Guid id, Guid negocioId, Guid conversaId, DateTimeOffset iniciadaEm, int duracaoMs, DesfechoRodadaAgente desfecho,
        MotivoSilencioRodada? motivoSilencio, int chamadas, int tokensEntrada, int tokensSaida, string? modelo, string? motivoFim,
        IEnumerable<string>? ferramentas, IEnumerable<string>? errosFerramenta, string? falhaProvedor, string? promptVersao)
    {
        Id = id;
        NegocioId = negocioId;
        ConversaId = conversaId;
        IniciadaEm = iniciadaEm;
        DuracaoMs = Math.Max(0, duracaoMs);
        Desfecho = desfecho;
        MotivoSilencio = motivoSilencio;
        Chamadas = Math.Max(0, chamadas);
        TokensEntrada = Math.Max(0, tokensEntrada);
        TokensSaida = Math.Max(0, tokensSaida);
        Modelo = CodigoSeguro(modelo, 100);
        MotivoFim = CodigoSeguro(motivoFim, TamanhoMaximoCodigo);
        Ferramentas = Lista(ferramentas);
        ErrosFerramenta = Lista(errosFerramenta);
        FalhaProvedor = CodigoSeguro(falhaProvedor, TamanhoMaximoCodigo);
        PromptVersao = CodigoSeguro(promptVersao, 20);
    }

    /// <summary>
    /// Reduz o texto a um código curto (letras, números, <c>_ . : -</c>): o que vier do provedor ou das ferramentas nunca grava
    /// espaço, quebra de linha nem texto livre. Vazio vira nulo.
    /// </summary>
    public static string? CodigoSeguro(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var limpo = new string(valor.Where(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.' or ':' or '-').ToArray());
        if (limpo.Length == 0)
            return null;

        return limpo.Length > maximo ? limpo[..maximo] : limpo;
    }

    private static string? Lista(IEnumerable<string>? itens)
    {
        if (itens is null)
            return null;

        var codigos = itens.Select(i => CodigoSeguro(i, TamanhoMaximoCodigo)).Where(i => i is not null).ToList();
        if (codigos.Count == 0)
            return null;

        var junto = string.Join(',', codigos);
        return junto.Length > TamanhoMaximoLista ? junto[..TamanhoMaximoLista] : junto;
    }
}
