namespace Plataforma.Infraestrutura.Idempotencia;

/// <summary>
/// Chave desta requisição HTTP, preenchida pelo filtro do endpoint (cabeçalho validado e hash canônico já calculado) e lida
/// pelo <see cref="ServicoIdempotencia"/> dentro da transação do serviço de domínio. Escopo de requisição (um por request).
/// </summary>
public sealed class ContextoIdempotencia
{
    public string? Chave { get; private set; }

    public string? Escopo { get; private set; }

    public string? HashRequisicao { get; private set; }

    public int StatusSucesso { get; private set; }

    /// <summary>A resposta desta requisição é uma repetição (o filtro adiciona <c>Idempotent-Replayed</c>).</summary>
    public bool Repeticao { get; set; }

    public bool TemChave => Chave is not null;

    public void Definir(string chave, string escopo, string hashRequisicao, int statusSucesso)
    {
        Chave = chave;
        Escopo = escopo;
        HashRequisicao = hashRequisicao;
        StatusSucesso = statusSucesso;
    }
}
