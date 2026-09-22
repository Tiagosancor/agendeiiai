using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;

namespace Plataforma.Infraestrutura.Notificacoes;

/// <summary>Provedor de WhatsApp para dev/testes — só loga, nunca envia de verdade (seção 4).</summary>
public sealed class MensageriaWhatsAppFake : IMensageriaWhatsApp
{
    private readonly ILogger<MensageriaWhatsAppFake> _logger;

    public MensageriaWhatsAppFake(ILogger<MensageriaWhatsAppFake> logger)
    {
        _logger = logger;
    }

    public Task EnviarAsync(TelefoneE164 telefone, string mensagem, CancellationToken cancellationToken = default)
    {
        // Único lugar do sistema onde um telefone e uma mensagem (que pode conter o código)
        // aparecem em texto puro no log — permitido só aqui, só em dev (seção 8.1.6).
        _logger.LogInformation("[MensageriaWhatsAppFake] Para: {Telefone} | Mensagem: {Mensagem}", telefone.Valor, mensagem);

        return Task.CompletedTask;
    }
}
