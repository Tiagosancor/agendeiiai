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

    protected UsoIa()
    {
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
