namespace Plataforma.Aplicacao.Notificacoes;

/// <summary>E-mail transacional (código, confirmação, Fale Conosco — seção 9). <c>Fake</c> em dev/testes; provedor real decidido: Resend (seção 4).</summary>
public interface IEmailSender
{
    Task EnviarAsync(
        string destinatario, string assunto, string corpoHtml, CancellationToken cancellationToken = default);
}
