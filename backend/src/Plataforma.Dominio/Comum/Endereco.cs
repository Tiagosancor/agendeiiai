namespace Plataforma.Dominio.Comum;

/// <summary>
/// Endereço reutilizado por negócio, usuário e profissional (seções 5 e 7).
/// Todos os campos são opcionais no nível do value object — quem decide o que é
/// obrigatório é quem usa (ex.: cadastro de negócio pode exigir tudo; um usuário
/// pode deixar em branco).
/// </summary>
public sealed record Endereco(
    string? Bairro,
    string? Cidade,
    string? Rua,
    string? Numero,
    string? Cep)
{
    public static Endereco Vazio { get; } = new(null, null, null, null, null);
}
