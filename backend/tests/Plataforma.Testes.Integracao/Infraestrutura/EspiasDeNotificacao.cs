using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;

namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>Substitui os provedores <c>Fake</c> nos testes — em vez de só logar, guarda o que foi "enviado" para os testes conferirem (ex.: extrair o código de verificação da mensagem).</summary>
public sealed class EspiaEmail : IEmailSender
{
    public List<(string Destinatario, string Assunto, string CorpoHtml)> Enviados { get; } = [];

    public Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken cancellationToken = default)
    {
        Enviados.Add((destinatario, assunto, corpoHtml));
        return Task.CompletedTask;
    }
}

public sealed class EspiaWhatsApp : IMensageriaWhatsApp
{
    public List<(TelefoneE164 Telefone, string Mensagem)> Enviados { get; } = [];

    public Task EnviarAsync(TelefoneE164 telefone, string mensagem, CancellationToken cancellationToken = default)
    {
        Enviados.Add((telefone, mensagem));
        return Task.CompletedTask;
    }
}
