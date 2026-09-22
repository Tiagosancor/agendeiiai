using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Notificacoes;

namespace Plataforma.Infraestrutura.Notificacoes;

/// <summary>Provedor de e-mail para dev/testes — só loga, nunca envia de verdade (seção 4).</summary>
public sealed class EmailSenderFake : IEmailSender
{
    private readonly ILogger<EmailSenderFake> _logger;

    public EmailSenderFake(ILogger<EmailSenderFake> logger)
    {
        _logger = logger;
    }

    public Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[EmailSenderFake] Para: {Destinatario} | Assunto: {Assunto} | Corpo: {CorpoHtml}",
            destinatario, assunto, corpoHtml);

        return Task.CompletedTask;
    }
}
