namespace Plataforma.Aplicacao.Cadastro;

/// <summary>Cadastro de negócio novo por autoatendimento (seção 6.5) com as proteções da seção 8.6.</summary>
public interface IServicoCadastro
{
    Task<IReadOnlyList<PlanoPublico>> ListarPlanosAsync(CancellationToken cancellationToken = default);

    Task<DisponibilidadeSlug> VerificarSlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Resposta idêntica exista o e-mail ou não (seção 8.6.2) — só o conteúdo do e-mail enviado muda.</summary>
    Task<ResultadoSolicitarCodigoCadastro> SolicitarCodigoAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Devolve o <c>tokenCadastro</c>, ou nulo com código errado/expirado.</summary>
    Task<string?> ValidarCodigoAsync(string email, string codigo, CancellationToken cancellationToken = default);

    Task<ResultadoCadastro> CadastrarAsync(DadosCadastro dados, string chaveIdempotencia, CancellationToken cancellationToken = default);
}

public sealed record PlanoPublico(
    Guid Id, string Nome, int MinimoProfissionais, int MaximoProfissionais,
    decimal PrecoMensal, decimal PrecoAnualPorMes, bool Destaque);

public sealed record DisponibilidadeSlug(bool Disponivel, string? Motivo);

public sealed record ResultadoSolicitarCodigoCadastro(bool LimiteExcedido);

public sealed record DadosCadastro(
    string TokenCadastro, Guid PlanoId, string Periodicidade,
    string NomeNegocio, string TipoNegocio, string Slug,
    string Nome, string Email, string Telefone, string Senha, bool AceiteTermos);

public enum ErroCadastro
{
    DadosInvalidos,
    TokenInvalido,
    SlugInvalido,
    SlugEmUso,
    EmailJaCadastrado,
    TesteJaUtilizado,
    ChaveIdempotenciaDeOutroCadastro,
}

public sealed record ResultadoCadastro(Guid? NegocioId, string? Slug, ErroCadastro? Erro, string? Mensagem)
{
    public bool Sucesso => Erro is null;

    public static ResultadoCadastro Ok(Guid negocioId, string slug) => new(negocioId, slug, null, null);

    public static ResultadoCadastro Falha(ErroCadastro erro, string mensagem) => new(null, null, erro, mensagem);
}
