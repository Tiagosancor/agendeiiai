namespace Plataforma.Dominio.Comissoes;

/// <summary>
/// Quem recebe comissão e deve vale/consumo (seção 7): um profissional **ou** um usuário que vende sem cadastro de
/// profissional (ex.: Recepcionista). Exatamente um dos dois — usuário vinculado a profissional entra como o profissional.
/// </summary>
public readonly record struct PessoaComissao
{
    public Guid? ProfissionalId { get; }

    public Guid? UsuarioId { get; }

    private PessoaComissao(Guid? profissionalId, Guid? usuarioId)
    {
        ProfissionalId = profissionalId;
        UsuarioId = usuarioId;
    }

    public static PessoaComissao Profissional(Guid profissionalId) => new(profissionalId, null);

    public static PessoaComissao Usuario(Guid usuarioId) => new(null, usuarioId);

    /// <summary>Lança <see cref="ArgumentException"/> se não vier exatamente um dos dois.</summary>
    public static PessoaComissao Criar(Guid? profissionalId, Guid? usuarioId) => (profissionalId, usuarioId) switch
    {
        ({ } p, null) => Profissional(p),
        (null, { } u) => Usuario(u),
        _ => throw new ArgumentException("Escolha um profissional ou um usuário."),
    };
}
