using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Usuarios;

/// <summary>
/// Pedido de "esqueci minha senha" do painel. O link enviado por e-mail carrega um token
/// aleatório; aqui fica só o hash (mesmo padrão do refresh token e do código de
/// confirmação — seção 8.1.2). Uso único, validade curta, e um pedido novo invalida os
/// anteriores ainda pendentes do mesmo usuário.
/// </summary>
public enum TipoRedefinicaoSenha
{
    Redefinicao = 0,
    Convite = 1,
}

public class RedefinicaoSenha : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid UsuarioId { get; private set; }

    public string Hash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiraEm { get; private set; }

    public DateTimeOffset? UsadaEm { get; private set; }

    /// <summary>Invalidada por um pedido mais novo, sem ter sido usada.</summary>
    public DateTimeOffset? InvalidadaEm { get; private set; }

    /// <summary>Convite (administrador) ou redefinição pedida pelo usuário. Só o convite verifica o e-mail ao ser usado.</summary>
    public TipoRedefinicaoSenha Tipo { get; private set; } = TipoRedefinicaoSenha.Redefinicao;

    /// <summary>SHA-256 do e-mail para onde o link foi — o convite só verifica o e-mail se ele ainda for o mesmo.</summary>
    public string? EmailDestinoHash { get; private set; }

    protected RedefinicaoSenha()
    {
    }

    private RedefinicaoSenha(Guid negocioId, Guid usuarioId, string hash, DateTimeOffset expiraEm)
    {
        NegocioId = negocioId;
        UsuarioId = usuarioId;
        Hash = hash;
        ExpiraEm = expiraEm;
    }

    public static RedefinicaoSenha Criar(
        Guid negocioId, Guid usuarioId, string hash, DateTimeOffset expiraEm,
        TipoRedefinicaoSenha tipo = TipoRedefinicaoSenha.Redefinicao, string? emailDestinoHash = null)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("O hash do token é obrigatório.", nameof(hash));

        return new RedefinicaoSenha(negocioId, usuarioId, hash, expiraEm) { Tipo = tipo, EmailDestinoHash = emailDestinoHash };
    }

    public bool Valida(DateTimeOffset agora) => UsadaEm is null && InvalidadaEm is null && ExpiraEm > agora;

    public void MarcarUsada(DateTimeOffset agora)
    {
        if (!Valida(agora))
            throw new InvalidOperationException("Link de redefinição inválido ou expirado.");

        UsadaEm = agora;
    }

    public void Invalidar(DateTimeOffset agora) => InvalidadaEm ??= agora;
}
