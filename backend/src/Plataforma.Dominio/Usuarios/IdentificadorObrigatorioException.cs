namespace Plataforma.Dominio.Usuarios;

/// <summary>O usuário ficaria sem e-mail, nome de usuário e telefone de login — não teria como entrar (400 <c>identificador_obrigatorio</c>).</summary>
public sealed class IdentificadorObrigatorioException()
    : InvalidOperationException("Informe pelo menos um modo de entrar: e-mail, nome de usuário ou telefone.");
