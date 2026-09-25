namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>Cloudflare Turnstile (seção 8.6.1) — variável <c>Captcha__ChaveSecreta</c>. Vazia = captcha desligado.</summary>
public sealed class OpcoesCaptcha
{
    public const string Secao = "Captcha";

    public string? ChaveSecreta { get; set; }
}
