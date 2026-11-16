using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Usuarios;

/// <summary>
/// Pedido de confirmação de e-mail feito pelo próprio usuário logado ("Minha conta"). Guarda só o hash HMAC do código
/// (prefixo de domínio próprio, nunca colide com o código de cliente), válido por 5 min, uso único, máximo de tentativas.
/// O e-mail pendente é apagado (nulo) ao usar ou invalidar — não fica como dado pessoal solto.
/// Só a entidade e a tabela (Fatia A); o serviço que a usa vem na Fatia B.
/// </summary>
public class VerificacaoEmailUsuario : EntidadeBase, IEntidadeDoNegocio
{
    public const int MaximoTentativasPadrao = 3;

    public Guid NegocioId { get; private set; }

    public Guid UsuarioId { get; private set; }

    /// <summary>E-mail pendente, normalizado; nulo depois de usado ou invalidado.</summary>
    public string? Email { get; private set; }

    public string CodigoHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiraEm { get; private set; }

    public int Tentativas { get; private set; }

    public int MaximoTentativas { get; private set; } = MaximoTentativasPadrao;

    public DateTimeOffset? UsadoEm { get; private set; }

    public DateTimeOffset? InvalidadoEm { get; private set; }

    protected VerificacaoEmailUsuario()
    {
    }

    public static VerificacaoEmailUsuario Criar(
        Guid negocioId, Guid usuarioId, string email, string codigoHash, DateTimeOffset expiraEm,
        int maximoTentativas = MaximoTentativasPadrao)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("O e-mail é obrigatório.", nameof(email));

        if (string.IsNullOrWhiteSpace(codigoHash))
            throw new ArgumentException("O hash do código é obrigatório.", nameof(codigoHash));

        return new VerificacaoEmailUsuario
        {
            NegocioId = negocioId,
            UsuarioId = usuarioId,
            Email = email.Trim().ToLowerInvariant(),
            CodigoHash = codigoHash,
            ExpiraEm = expiraEm,
            MaximoTentativas = maximoTentativas,
        };
    }

    public bool Pendente(DateTimeOffset agora) =>
        UsadoEm is null && InvalidadoEm is null && Tentativas < MaximoTentativas && ExpiraEm > agora;

    public void RegistrarTentativa() => Tentativas++;

    public void MarcarUsada(DateTimeOffset agora)
    {
        UsadoEm = agora;
        Email = null;
    }

    public void Invalidar(DateTimeOffset agora)
    {
        InvalidadoEm ??= agora;
        Email = null;
    }
}
