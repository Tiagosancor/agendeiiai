using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.WhatsAppAgente;

/// <summary>
/// Uma chamada ao modelo de IA (Fase 2 do agente). Guarda só números — modelo e tokens —, nunca texto de mensagem,
/// prompt ou resposta. Base dos limites por negócio (chamadas/dia e cota mensal de tokens).
/// </summary>
public class UsoIa : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid ConversaId { get; private set; }

    public string Modelo { get; private set; } = string.Empty;

    public int TokensEntrada { get; private set; }

    public int TokensSaida { get; private set; }

    /// <summary>Falso quando o provedor falhou (as chamadas que falham também contam para o limite diário).</summary>
    public bool Sucesso { get; private set; }

    /// <summary>Motivo de término informado pelo provedor (ex.: STOP, MAX_TOKENS); só código, nunca texto.</summary>
    public string? MotivoFim { get; private set; }

    /// <summary>Tempo da chamada ao provedor, em milissegundos.</summary>
    public int? LatenciaMs { get; private set; }

    /// <summary>Volta do laço de ferramentas em que a chamada foi feita (1 = primeira).</summary>
    public int? Iteracao { get; private set; }

    /// <summary>Tipo da falha do provedor (ex.: <c>http</c>, <c>timeout</c>); só quando <see cref="Sucesso"/> é falso.</summary>
    public string? FalhaTipo { get; private set; }

    public int? StatusHttp { get; private set; }

    /// <summary>Rodada (<c>rodadas_agente</c>) a que a chamada pertence. Sem FK: a linha da rodada é gravada depois.</summary>
    public Guid? RodadaId { get; private set; }

    protected UsoIa()
    {
    }

    public UsoIa(
        Guid negocioId, Guid conversaId, string modelo, int tokensEntrada, int tokensSaida, bool sucesso,
        string? motivoFim, int latenciaMs, int iteracao, string? falhaTipo, int? statusHttp, Guid? rodadaId)
        : this(negocioId, conversaId, modelo, tokensEntrada, tokensSaida, sucesso)
    {
        MotivoFim = RodadaAgente.CodigoSeguro(motivoFim, RodadaAgente.TamanhoMaximoCodigo);
        LatenciaMs = Math.Max(0, latenciaMs);
        Iteracao = Math.Max(0, iteracao);
        FalhaTipo = RodadaAgente.CodigoSeguro(falhaTipo, RodadaAgente.TamanhoMaximoCodigo);
        StatusHttp = statusHttp;
        RodadaId = rodadaId;
    }

    public UsoIa(Guid negocioId, Guid conversaId, string modelo, int tokensEntrada, int tokensSaida, bool sucesso)
    {
        var nome = modelo ?? string.Empty;
        NegocioId = negocioId;
        ConversaId = conversaId;
        Modelo = nome.Length > 100 ? nome[..100] : nome;
        TokensEntrada = Math.Max(0, tokensEntrada);
        TokensSaida = Math.Max(0, tokensSaida);
        Sucesso = sucesso;
    }
}
