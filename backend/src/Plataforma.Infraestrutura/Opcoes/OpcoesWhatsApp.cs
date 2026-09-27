using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>
/// Provedor de WhatsApp (seção 4) — <c>Fake</c> em dev/testes, <c>Oficial</c> (API Cloud da
/// Meta) ou <c>EvolutionApi</c> (não oficial). Provedores não oficiais violam os termos do
/// WhatsApp e podem levar ao banimento do número: o <c>EvolutionApi</c> só sobe com
/// <see cref="PermitirNaoOficial"/> ligado explicitamente, e nunca é o único canal do código de
/// confirmação — o e-mail sai sempre em paralelo (seção 8.1).
/// </summary>
public sealed class OpcoesWhatsApp : IValidatableObject
{
    public const string Secao = "WhatsApp";

    public ProvedorWhatsApp Provedor { get; set; } = ProvedorWhatsApp.Fake;

    /// <summary>Precisa estar <c>true</c> para o provedor <see cref="ProvedorWhatsApp.EvolutionApi"/> subir. Padrão <c>false</c>.</summary>
    public bool PermitirNaoOficial { get; set; }

    public OpcoesMetaWhatsApp Meta { get; set; } = new();

    public OpcoesEvolutionApi Evolution { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Provedor == ProvedorWhatsApp.Oficial)
        {
            if (string.IsNullOrWhiteSpace(Meta.Token))
                yield return new ValidationResult("WhatsApp__Meta__Token é obrigatório quando WhatsApp__Provedor=Oficial.", [nameof(Meta)]);

            if (string.IsNullOrWhiteSpace(Meta.NumeroTelefoneId))
                yield return new ValidationResult("WhatsApp__Meta__NumeroTelefoneId é obrigatório quando WhatsApp__Provedor=Oficial.", [nameof(Meta)]);
        }

        if (Provedor == ProvedorWhatsApp.EvolutionApi)
        {
            if (!PermitirNaoOficial)
                yield return new ValidationResult(
                    "WhatsApp__Provedor=EvolutionApi é um provedor não oficial (viola os termos do WhatsApp e pode levar ao banimento do número) — só sobe com WhatsApp__PermitirNaoOficial=true.",
                    [nameof(PermitirNaoOficial)]);

            if (!Uri.TryCreate(Evolution.UrlBase, UriKind.Absolute, out _))
                yield return new ValidationResult("WhatsApp__Evolution__UrlBase é obrigatório (URL absoluta) quando WhatsApp__Provedor=EvolutionApi.", [nameof(Evolution)]);

            if (string.IsNullOrWhiteSpace(Evolution.ChaveApi))
                yield return new ValidationResult("WhatsApp__Evolution__ChaveApi é obrigatório quando WhatsApp__Provedor=EvolutionApi.", [nameof(Evolution)]);

            if (string.IsNullOrWhiteSpace(Evolution.NomeInstancia))
                yield return new ValidationResult("WhatsApp__Evolution__NomeInstancia é obrigatório quando WhatsApp__Provedor=EvolutionApi.", [nameof(Evolution)]);

            if (string.IsNullOrWhiteSpace(Evolution.SegredoWebhook) || Evolution.SegredoWebhook.Length < 16)
                yield return new ValidationResult("WhatsApp__Evolution__SegredoWebhook é obrigatório (16+ caracteres) quando WhatsApp__Provedor=EvolutionApi.", [nameof(Evolution)]);

            if (Evolution.TimeoutSegundos is < 1 or > 30)
                yield return new ValidationResult("WhatsApp__Evolution__TimeoutSegundos precisa ficar entre 1 e 30.", [nameof(Evolution)]);
        }
    }
}

public enum ProvedorWhatsApp
{
    Fake = 1,
    Oficial = 2,
    EvolutionApi = 3,
}

public sealed class OpcoesMetaWhatsApp
{
    public string? Token { get; set; }

    public string? NumeroTelefoneId { get; set; }

    /// <summary>Nome do template de autenticação aprovado na Meta (seção 12, pré-requisito ainda pendente) — o código vai como parâmetro dele.</summary>
    public string NomeTemplateCodigo { get; set; } = "codigo_confirmacao";
}

/// <summary>Instância do Evolution API (seção 4) — tudo por configuração, nunca no código.</summary>
public sealed class OpcoesEvolutionApi
{
    /// <summary>Ex.: <c>http://evolution:8080</c> no docker-compose.</summary>
    public string? UrlBase { get; set; }

    /// <summary>Chave de API da instância (cabeçalho <c>apikey</c>).</summary>
    public string? ChaveApi { get; set; }

    public string? NomeInstancia { get; set; }

    /// <summary>
    /// Segredo compartilhado do webhook de status: a instância manda no cabeçalho
    /// <c>X-Webhook-Token</c> ou em <c>?token=</c> na URL configurada nela. Sem ele, o webhook recusa.
    /// </summary>
    public string? SegredoWebhook { get; set; }

    /// <summary>Tempo de cada tentativa de envio. Curto: o cliente espera a resposta do "enviar código".</summary>
    public int TimeoutSegundos { get; set; } = 5;
}
