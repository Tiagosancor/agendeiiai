using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>
/// Provedor de WhatsApp (seção 4) — <c>Fake</c> em dev/testes, <c>Oficial</c> (API Cloud
/// da Meta) em produção. Provedores não oficiais nunca entram aqui como único canal do
/// código de confirmação (seção 4/8.1) — <see cref="PermitirNaoOficial"/> existe só para
/// documentar a flag futura, padrão sempre <c>false</c> nesta fase.
/// </summary>
public sealed class OpcoesWhatsApp : IValidatableObject
{
    public const string Secao = "WhatsApp";

    public ProvedorWhatsApp Provedor { get; set; } = ProvedorWhatsApp.Fake;

    public bool PermitirNaoOficial { get; set; }

    public OpcoesMetaWhatsApp Meta { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provedor != ProvedorWhatsApp.Oficial)
            yield break;

        if (string.IsNullOrWhiteSpace(Meta.Token))
            yield return new ValidationResult("WhatsApp__Meta__Token é obrigatório quando WhatsApp__Provedor=Oficial.", [nameof(Meta)]);

        if (string.IsNullOrWhiteSpace(Meta.NumeroTelefoneId))
            yield return new ValidationResult("WhatsApp__Meta__NumeroTelefoneId é obrigatório quando WhatsApp__Provedor=Oficial.", [nameof(Meta)]);
    }
}

public enum ProvedorWhatsApp
{
    Fake = 1,
    Oficial = 2,
}

public sealed class OpcoesMetaWhatsApp
{
    public string? Token { get; set; }

    public string? NumeroTelefoneId { get; set; }

    /// <summary>Nome do template de autenticação aprovado na Meta (seção 12, pré-requisito ainda pendente) — o código vai como parâmetro dele.</summary>
    public string NomeTemplateCodigo { get; set; } = "codigo_confirmacao";
}
