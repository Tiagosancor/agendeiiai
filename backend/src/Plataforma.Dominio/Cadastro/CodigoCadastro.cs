using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Cadastro;

/// <summary>
/// Código que confirma o e-mail no cadastro de um negócio novo (seção 6.5) — mesmas regras do
/// código do cliente final (seção 8.1): só o hash, 5 minutos, uso único, 5 tentativas, e um
/// código novo invalida o anterior. Não pertence a negócio nenhum (o negócio ainda não existe).
/// </summary>
public class CodigoCadastro : EntidadeBase
{
    private const int MaximoTentativas = 5;

    public string Email { get; private set; } = string.Empty;

    public string HashCodigo { get; private set; } = string.Empty;

    public DateTimeOffset ExpiraEm { get; private set; }

    public int TentativasRestantes { get; private set; } = MaximoTentativas;

    public bool Usado { get; private set; }

    public bool Invalidado { get; private set; }

    protected CodigoCadastro()
    {
    }

    public static CodigoCadastro Criar(string email, string hashCodigo, DateTimeOffset agora) => new()
    {
        Email = NormalizarEmail(email),
        HashCodigo = hashCodigo,
        ExpiraEm = agora.AddMinutes(5),
    };

    public bool EstaValido(DateTimeOffset agora) => !Usado && !Invalidado && agora <= ExpiraEm && TentativasRestantes > 0;

    public bool ConferirEMarcar(string hashInformado, DateTimeOffset agora)
    {
        if (!EstaValido(agora))
            return false;

        if (!string.Equals(HashCodigo, hashInformado, StringComparison.Ordinal))
        {
            TentativasRestantes--;
            return false;
        }

        Usado = true;
        return true;
    }

    public static string NormalizarEmail(string email) => email.Trim().ToLowerInvariant();
}
