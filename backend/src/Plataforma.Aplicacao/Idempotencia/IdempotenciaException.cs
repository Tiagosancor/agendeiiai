namespace Plataforma.Aplicacao.Idempotencia;

/// <summary>Resposta de erro do mecanismo (422 corpo diferente, 409 em andamento). Não é transitória: a estratégia não a repete.</summary>
public sealed class IdempotenciaException : Exception
{
    public const string CodigoChaveReutilizada = "idempotency_key_reutilizada";
    public const string CodigoEmAndamento = "operacao_em_andamento";
    public const string CodigoChaveInvalida = "idempotency_key_invalida";
    public const string CodigoChaveObrigatoria = "idempotency_key_obrigatoria";

    public IdempotenciaException(string codigo, int statusHttp, string mensagem, int? retryAfterSegundos = null)
        : base(mensagem)
    {
        Codigo = codigo;
        StatusHttp = statusHttp;
        RetryAfterSegundos = retryAfterSegundos;
    }

    public string Codigo { get; }

    public int StatusHttp { get; }

    public int? RetryAfterSegundos { get; }
}
