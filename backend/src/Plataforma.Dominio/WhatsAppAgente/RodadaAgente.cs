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
        MotivoFim = MotivoFimFechado(motivoFim);
        Ferramentas = Lista(ferramentas);
        ErrosFerramenta = Lista(errosFerramenta);
        FalhaProvedor = FalhaProvedorFechada(falhaProvedor);
        PromptVersao = CodigoSeguro(promptVersao, 20);
    }

    /// <summary>Marcador fixo para qualquer valor fora das listas fechadas abaixo.</summary>
    public const string Desconhecido = "DESCONHECIDO";

    /// <summary>
    /// Únicos valores aceitos em <c>motivo_fim</c>: os motivos de término do Gemini e os marcadores do orquestrador. Qualquer outro
    /// valor (inclusive texto que lembre dado pessoal) vira <see cref="Desconhecido"/>. A garantia de privacidade é ESTA lista, não o
    /// filtro de caracteres de <see cref="CodigoSeguro"/>.
    /// </summary>
    private static readonly HashSet<string> MotivosFimAceitos = new(StringComparer.Ordinal)
    {
        "STOP", "MAX_TOKENS", "SAFETY", "RECITATION", "OTHER", "BLOCKLIST", "PROHIBITED_CONTENT", "SPII", "MALFORMED_FUNCTION_CALL",
        "LANGUAGE", "IMAGE_SAFETY", "UNEXPECTED_TOOL_CALL", "FINISH_REASON_UNSPECIFIED",
        "LACO_FERRAMENTAS", "ORCAMENTO_TEMPO", "LIMITE_CUSTO", "EXCECAO", "TEXTO_DESCARTADO", Desconhecido,
    };

    /// <summary>Únicos tipos de falha do provedor aceitos (os que o provedor de IA lança).</summary>
    private static readonly HashSet<string> TiposFalhaAceitos = new(StringComparer.Ordinal)
    {
        "http", "timeout", "rede", "resposta_invalida",
    };

    /// <summary>Motivo de término dentro da lista fechada (nulo continua nulo; o resto, <see cref="Desconhecido"/>).</summary>
    public static string? MotivoFimFechado(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var normal = valor.Trim().ToUpperInvariant();
        return MotivosFimAceitos.Contains(normal) ? normal : Desconhecido;
    }

    /// <summary>Tipo de falha do provedor dentro da lista fechada (nulo continua nulo; o resto, <see cref="Desconhecido"/>).</summary>
    public static string? TipoFalhaFechado(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var normal = valor.Trim().ToLowerInvariant();
        return TiposFalhaAceitos.Contains(normal) ? normal : Desconhecido;
    }

    /// <summary>Falha do provedor como <c>tipo</c> ou <c>tipo:status</c>, itens separados por vírgula; cada item é revalidado.</summary>
    public static string? FalhaProvedorFechada(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var itens = valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(item =>
        {
            var partes = item.Split(':', 2);
            var tipo = TipoFalhaFechado(partes[0]);
            return partes.Length == 2 && partes[1].Length is >= 1 and <= 3 && partes[1].All(char.IsAsciiDigit) ? $"{tipo}:{partes[1]}" : tipo;
        }).Where(i => i is not null).Distinct().ToList();
        if (itens.Count == 0)
            return null;

        var junto = string.Join(',', itens);
        return junto.Length > TamanhoMaximoCodigo ? junto[..TamanhoMaximoCodigo] : junto;
    }

    /// <summary>
    /// Formato defensivo: reduz o texto a letras ASCII, números e <c>_ . : -</c> (nada de espaço, quebra de linha ou acento). NÃO
    /// garante privacidade — um telefone ou parte de um e-mail passa; por isso só é usado em valores que já vêm de catálogo
    /// (nome do modelo, versão do prompt, nomes de ferramenta e códigos de erro) e nunca em campos livres.
    /// </summary>
    /// <remarks>
    /// Reduz o texto a um código curto (letras, números, <c>_ . : -</c>): o que vier do provedor ou das ferramentas nunca grava
    /// espaço, quebra de linha nem texto livre. Vazio vira nulo.
    /// </remarks>
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
