using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Notificacoes;

/// <summary>
/// API Cloud da Meta (seção 4). Manda o template de autenticação aprovado (o código como
/// parâmetro), não texto livre — fora da janela de 24h de atendimento, a Meta só aceita
/// mensagens de negócio por template. Depende do cadastro na Meta ainda pendente (seção
/// 12); sem ele, chamadas aqui falham com 401/403 da própria API da Meta — o
/// <see cref="OpcoesWhatsApp"/> só é validado (exige token) quando o provedor é ativado.
/// </summary>
public sealed class MensageriaWhatsAppOficial : IMensageriaWhatsApp
{
    private readonly HttpClient _httpClient;
    private readonly OpcoesWhatsApp _opcoes;

    public MensageriaWhatsAppOficial(HttpClient httpClient, IOptions<OpcoesWhatsApp> opcoes)
    {
        _httpClient = httpClient;
        _opcoes = opcoes.Value;
    }

    /// <summary>
    /// <paramref name="mensagem"/> aqui é só o texto livre (usado no corpo do template, se
    /// ele tiver um parâmetro de texto) — o código de verificação é o único caso real hoje,
    /// então tratamos a mensagem inteira como o único parâmetro do template configurado.
    /// </summary>
    public async Task EnviarAsync(TelefoneE164 telefone, string mensagem, CancellationToken cancellationToken = default)
    {
        var requisicao = new HttpRequestMessage(
            HttpMethod.Post, $"https://graph.facebook.com/v20.0/{_opcoes.Meta.NumeroTelefoneId}/messages")
        {
            Headers = { { "Authorization", $"Bearer {_opcoes.Meta.Token}" } },
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                to = telefone.Valor.TrimStart('+'),
                type = "template",
                template = new
                {
                    name = _opcoes.Meta.NomeTemplateCodigo,
                    language = new { code = "pt_BR" },
                    components = new object[]
                    {
                        new { type = "body", parameters = new object[] { new { type = "text", text = mensagem } } },
                    },
                },
            }),
        };

        using var resposta = await _httpClient.SendAsync(requisicao, cancellationToken);
        resposta.EnsureSuccessStatusCode();
    }
}
