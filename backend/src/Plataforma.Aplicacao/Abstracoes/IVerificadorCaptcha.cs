namespace Plataforma.Aplicacao.Abstracoes;

/// <summary>Captcha invisível (Cloudflare Turnstile — seção 8.6.1). Sem chave configurada, fica desligado e sempre aprova.</summary>
public interface IVerificadorCaptcha
{
    bool Ativo { get; }

    Task<bool> ValidarAsync(string? tokenCaptcha, string? ip, CancellationToken cancellationToken = default);
}
