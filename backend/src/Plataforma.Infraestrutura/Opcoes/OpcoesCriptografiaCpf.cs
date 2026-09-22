using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>
/// Chave de criptografia do CPF (seção 8.4) — sempre fora do repositório, versionada.
/// Gere uma chave nova com: <c>openssl rand -base64 32</c>.
/// </summary>
public sealed class OpcoesCriptografiaCpf
{
    public const string Secao = "Cpf";

    /// <summary>Identifica qual chave cifrou um valor — necessário para rotação de chave no futuro.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Cpf__ChaveId é obrigatório.")]
    public string ChaveId { get; set; } = string.Empty;

    /// <summary>Chave AES-256 em base64 (32 bytes decodificados).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Cpf__ChaveBase64 é obrigatório.")]
    public string ChaveBase64 { get; set; } = string.Empty;
}
