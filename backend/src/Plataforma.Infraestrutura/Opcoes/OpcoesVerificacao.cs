using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>Código de confirmação do cliente final (seção 8.1) — limites de abuso e a chave que pimenta o hash do código.</summary>
public sealed class OpcoesVerificacao
{
    public const string Secao = "Verificacao";

    /// <summary>Pimenta do HMAC que gera o hash do código (seção 8.1.2) — nunca o próprio código, só a chave que o protege. Gere com: <c>openssl rand -base64 32</c>.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Verificacao__ChaveHmac é obrigatório.")]
    public string ChaveHmac { get; set; } = string.Empty;

    [Range(1, 20)]
    public int MaximoCodigosPorTelefonePorHora { get; set; } = 3;

    [Range(1, 50)]
    public int MaximoCodigosPorTelefonePorDia { get; set; } = 10;

    /// <summary>Teto diário de códigos por WhatsApp por negócio (seção 8.1.5) — ao atingir, os códigos seguintes vão só por e-mail.</summary>
    [Range(1, 100000)]
    public int MaximoWhatsAppPorNegocioPorDia { get; set; } = 500;
}
