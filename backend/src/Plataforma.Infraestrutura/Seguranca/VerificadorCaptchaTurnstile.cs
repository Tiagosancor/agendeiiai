using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Seguranca;

public sealed class VerificadorCaptchaTurnstile : IVerificadorCaptcha
{
    private const string UrlVerificacao = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly HttpClient _http;
    private readonly OpcoesCaptcha _opcoes;
    private readonly ILogger<VerificadorCaptchaTurnstile> _logger;

    public VerificadorCaptchaTurnstile(HttpClient http, IOptions<OpcoesCaptcha> opcoes, ILogger<VerificadorCaptchaTurnstile> logger)
    {
        _http = http;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public bool Ativo => !string.IsNullOrWhiteSpace(_opcoes.ChaveSecreta);

    public async Task<bool> ValidarAsync(string? tokenCaptcha, string? ip, CancellationToken cancellationToken = default)
    {
        if (!Ativo)
            return true;

        if (string.IsNullOrWhiteSpace(tokenCaptcha))
            return false;

        var campos = new Dictionary<string, string> { ["secret"] = _opcoes.ChaveSecreta!, ["response"] = tokenCaptcha };
        if (!string.IsNullOrWhiteSpace(ip))
            campos["remoteip"] = ip;

        try
        {
            using var resposta = await _http.PostAsync(UrlVerificacao, new FormUrlEncodedContent(campos), cancellationToken);
            var corpo = await resposta.Content.ReadFromJsonAsync<RespostaTurnstile>(cancellationToken);
            return corpo?.Success == true;
        }
        catch (Exception excecao) when (excecao is HttpRequestException or TaskCanceledException)
        {
            // Falha fechada: sem confirmar o captcha, não passa.
            _logger.LogWarning(excecao, "Falha ao verificar o captcha no Turnstile.");
            return false;
        }
    }

    private sealed record RespostaTurnstile(bool Success);
}
