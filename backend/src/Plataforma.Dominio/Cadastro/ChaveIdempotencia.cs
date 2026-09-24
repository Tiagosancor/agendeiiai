using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Cadastro;

/// <summary>
/// <c>Idempotency-Key</c> já usada (seção 6.5): gravada na mesma transação da operação, com
/// índice único (escopo, chave). Clique duplo ou reenvio depois de queda de conexão encontra a
/// chave e recebe o resultado da primeira vez, em vez de criar de novo.
/// </summary>
public class ChaveIdempotencia : EntidadeBase
{
    public const int TamanhoMaximo = 100;

    public string Escopo { get; private set; } = string.Empty;

    public string Chave { get; private set; } = string.Empty;

    /// <summary>Quem usou a chave — reenvio com a mesma chave e outro e-mail não recebe o resultado.</summary>
    public string Email { get; private set; } = string.Empty;

    public Guid? NegocioId { get; private set; }

    protected ChaveIdempotencia()
    {
    }

    public ChaveIdempotencia(string escopo, string chave, string email)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length > TamanhoMaximo)
            throw new ArgumentException("Chave de idempotência inválida.", nameof(chave));

        Escopo = escopo;
        Chave = chave.Trim();
        Email = CodigoCadastro.NormalizarEmail(email);
    }

    public void Concluir(Guid negocioId) => NegocioId = negocioId;
}
