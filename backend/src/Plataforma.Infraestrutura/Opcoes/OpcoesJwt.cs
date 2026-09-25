using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>
/// Configuração do JWT de acesso do painel (seção 4: "JWT de curta duração"). Gere um
/// segredo novo com: <c>openssl rand -base64 64</c>.
/// </summary>
public sealed class OpcoesJwt
{
    public const string Secao = "Jwt";

    [Required(AllowEmptyStrings = false, ErrorMessage = "Jwt__ChaveSecreta é obrigatório.")]
    [MinLength(32, ErrorMessage = "Jwt__ChaveSecreta precisa ter pelo menos 32 caracteres.")]
    public string ChaveSecreta { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "Jwt__Emissor é obrigatório.")]
    public string Emissor { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "Jwt__Audiencia é obrigatório.")]
    public string Audiencia { get; set; } = string.Empty;

    /// <summary>Validade do access token, em minutos. Curto de propósito — o refresh token é quem dura.</summary>
    [Range(1, 120)]
    public int AccessTokenMinutos { get; set; } = 15;

    /// <summary>Validade do refresh token, em dias.</summary>
    [Range(1, 365)]
    public int RefreshTokenDias { get; set; } = 30;

    /// <summary>Audiência do token da administração da plataforma — nunca aceita como token do painel, nem o contrário.</summary>
    public string AudienciaPlataforma => $"{Audiencia}:plataforma";
}
