using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Idempotencia;

/// <summary>
/// <c>Idempotency-Key</c> já processada numa operação do painel: reivindicada na <b>mesma transação</b> do efeito (primeira
/// instrução mutável) e concluída com o resultado mínimo antes do commit, então um registro confirmado sempre tem resultado.
/// Chave única por (negócio, principal, escopo, chave): outro usuário, negócio ou endpoint com a mesma chave não colide.
/// O corpo da requisição nunca é guardado — só o hash canônico, para detectar a mesma chave com outra intenção.
/// </summary>
public class RequisicaoIdempotente : EntidadeBase, IEntidadeDoNegocio
{
    public const int TamanhoMaximoChave = 100;

    public const int TamanhoMaximoResultadoBytes = 8192;

    public Guid NegocioId { get; private set; }

    /// <summary>Quem enviou: <c>u:{usuarioId}</c> no painel.</summary>
    public string Principal { get; private set; } = string.Empty;

    /// <summary>Endpoint (método + rota-modelo), ex.: <c>POST painel/vendas</c>.</summary>
    public string Escopo { get; private set; } = string.Empty;

    public string Chave { get; private set; } = string.Empty;

    /// <summary>SHA-256 (hex) da representação canônica da intenção (DTO + valores de rota).</summary>
    public string HashRequisicao { get; private set; } = string.Empty;

    /// <summary>Zero enquanto a operação não concluiu; um registro confirmado nunca fica com zero.</summary>
    public int StatusHttp { get; private set; }

    /// <summary>JSON mínimo para reconstruir a resposta (ids, totais) — nunca token, telefone ou corpo integral.</summary>
    public string Resultado { get; private set; } = "{}";

    public DateTimeOffset ExpiraEm { get; private set; }

    protected RequisicaoIdempotente()
    {
    }
}
