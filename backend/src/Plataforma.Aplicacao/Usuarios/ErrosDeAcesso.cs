namespace Plataforma.Aplicacao.Usuarios;

/// <summary>
/// Dado de acesso inválido (400). <see cref="Codigo"/> vai no corpo (<c>codigo</c>): <c>identificador_obrigatorio</c>,
/// <c>email_invalido</c>, <c>nome_usuario_invalido</c>, <c>nome_usuario_reservado</c>, <c>telefone_invalido</c>,
/// <c>senha_fraca</c>, <c>senha_incorreta</c>, <c>convite_sem_email</c>, <c>codigo_invalido</c>. Herda de
/// <see cref="ArgumentException"/> para os <c>catch</c> antigos continuarem devolvendo 400.
/// </summary>
public sealed class ErroDeAcessoException(string codigo, string mensagem) : ArgumentException(mensagem)
{
    public string Codigo { get; } = codigo;
}

/// <summary>
/// E-mail, nome de usuário ou telefone de login já em uso por outra conta (409 <c>identificador_indisponivel</c>).
/// Traz só o <see cref="Campo"/> (<c>email</c> | <c>nomeUsuario</c> | <c>telefoneLogin</c>) e, para o nome de usuário, uma
/// sugestão livre — **nunca** o valor (a mensagem vai ao corpo da resposta e pode cair em log).
/// </summary>
public sealed class IdentificadorIndisponivelException(string campo, string? sugestao = null)
    : Exception("Este identificador não pode ser usado para entrar.")
{
    public const string CampoEmail = "email";
    public const string CampoNomeUsuario = "nomeUsuario";
    public const string CampoTelefoneLogin = "telefoneLogin";

    public string Campo { get; } = campo;

    public string? Sugestao { get; } = sugestao;
}

/// <summary>Excesso de tentativas ou de conflitos (429 <c>muitas_tentativas</c>).</summary>
public sealed class MuitasTentativasException() : Exception("Muitas tentativas. Aguarde alguns minutos e tente de novo.");
