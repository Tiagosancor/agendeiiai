namespace Plataforma.Aplicacao.Idempotencia;

/// <summary>
/// <c>Idempotency-Key</c> do painel (opcional). O serviço de domínio chama <see cref="ReivindicarAsync{T}"/> como <b>primeira
/// instrução mutável</b> da própria transação (antes de advisory locks e <c>FOR UPDATE</c>) e <see cref="ConcluirAsync{T}"/> antes
/// do commit: a chave, o efeito e o resultado confirmam juntos. Sem chave (ou com o mecanismo desligado) tudo é no-op.
/// </summary>
public interface IServicoIdempotencia
{
    /// <summary>Há chave válida nesta requisição e o mecanismo está ligado.</summary>
    bool Ativa { get; }

    /// <summary>A resposta desta requisição é a repetição de uma anterior (não reaplicar efeitos nem avisos).</summary>
    bool Repeticao { get; }

    /// <summary>
    /// Devolve o resultado já confirmado da mesma chave e intenção (e marca <see cref="Repeticao"/>), ou <c>null</c> se esta
    /// requisição acabou de reivindicar a chave (ou não há chave). Outra intenção com a mesma chave:
    /// <see cref="IdempotenciaException"/> 422. Duplicado concorrente que espera demais: 409.
    /// </summary>
    Task<T?> ReivindicarAsync<T>(CancellationToken cancellationToken) where T : class;

    /// <summary>Grava o resultado mínimo na linha reivindicada (mesma transação). No-op se esta requisição não reivindicou.</summary>
    Task ConcluirAsync<T>(T resultado, CancellationToken cancellationToken) where T : class;

    /// <summary>Id estável derivado da chave (ex.: cliente do cadastro rápido), igual em toda repetição; <c>null</c> sem chave.</summary>
    Guid? IdDerivado(string finalidade);
}
