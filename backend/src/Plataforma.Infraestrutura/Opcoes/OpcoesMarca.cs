using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>
/// Configuração de marca (white-label — seção 5). Vem sempre de variáveis de
/// ambiente (prefixo <c>Marca__</c>), nunca de código. O nome do produto só
/// aparece no rodapé, no remetente do código de WhatsApp e no painel — nunca em
/// identificadores, textos de negócio ou nomes de tabela.
/// </summary>
public sealed class OpcoesMarca
{
    public const string Secao = "Marca";

    /// <summary>Nome do produto exibido no rodapé, no painel e no remetente do WhatsApp. Vem 100% de configuração.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Marca__NomeProduto é obrigatório.")]
    public string NomeProduto { get; set; } = string.Empty;

    /// <summary>Domínio base usado para montar os subdomínios dos negócios (ex.: "{slug}.dominio-configurado.com").</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Marca__Dominio é obrigatório.")]
    public string Dominio { get; set; } = string.Empty;

    /// <summary>E-mail remetente padrão (confirmações, código de verificação).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Marca__EmailRemetente é obrigatório.")]
    [EmailAddress(ErrorMessage = "Marca__EmailRemetente precisa ser um e-mail válido.")]
    public string EmailRemetente { get; set; } = string.Empty;

    /// <summary>Esquema da página pública do negócio, para montar links de e-mail (seção 6.3: cancelar/remarcar, .ics).</summary>
    public string EsquemaUrlPublica { get; set; } = "https";

    /// <summary>Porta da página pública, só em dev (ex.: 3000) — nula em produção, onde a porta é a padrão do esquema.</summary>
    public int? PortaUrlPublica { get; set; }
}
