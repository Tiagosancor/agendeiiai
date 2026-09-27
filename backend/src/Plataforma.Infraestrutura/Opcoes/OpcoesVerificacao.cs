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

    /// <summary>Validade do código (seção 8.1.2).</summary>
    [Range(1, 30)]
    public int ValidadeCodigoMinutos { get; set; } = 5;

    /// <summary>Tentativas de validação de cada código (seção 8.1.2) — errou todas, precisa de um código novo.</summary>
    [Range(1, 10)]
    public int MaximoTentativasValidacao { get; set; } = 3;

    /// <summary>Quantas vezes o mesmo código pode ser reenviado (<c>POST /publico/codigos/reenviar</c>). O pedido seguinte é negado.</summary>
    [Range(0, 10)]
    public int MaximoReenvios { get; set; } = 3;
}
