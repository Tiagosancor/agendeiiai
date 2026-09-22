using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Notificacoes;

/// <summary>Provedor de e-mail real (seção 4, decidido na Sprint 0). API HTTP simples — sem SDK oficial em C#.</summary>
public sealed class EmailSenderResend : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly OpcoesEmail _opcoesEmail;
    private readonly OpcoesMarca _opcoesMarca;

    public EmailSenderResend(HttpClient httpClient, IOptions<OpcoesEmail> opcoesEmail, IOptions<OpcoesMarca> opcoesMarca)
    {
        _httpClient = httpClient;
        _opcoesEmail = opcoesEmail.Value;
        _opcoesMarca = opcoesMarca.Value;
    }

    public async Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken cancellationToken = default)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Headers = { { "Authorization", $"Bearer {_opcoesEmail.Resend.ApiKey}" } },
            Content = JsonContent.Create(new
            {
                from = $"{_opcoesMarca.NomeProduto} <{_opcoesMarca.EmailRemetente}>",
                to = new[] { destinatario },
                subject = assunto,
                html = corpoHtml,
            }),
        };

        using var resposta = await _httpClient.SendAsync(requisicao, cancellationToken);
        resposta.EnsureSuccessStatusCode();
    }
}
