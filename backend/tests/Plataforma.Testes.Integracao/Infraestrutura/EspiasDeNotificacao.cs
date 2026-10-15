using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;

namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>Substitui os provedores <c>Fake</c> nos testes — em vez de só logar, guarda o que foi "enviado" para os testes conferirem (ex.: extrair o código de verificação da mensagem).</summary>
public sealed class EspiaEmail : IEmailSender
{
    public List<(string Destinatario, string Assunto, string CorpoHtml)> Enviados { get; } = [];

    /// <summary>Simula o provedor de e-mail fora do ar: lança, como o Resend lançaria. Singleton — quem liga, desliga.</summary>
    public bool Falhar { get; set; }

    /// <summary>Com <see cref="Falhar"/>, o status HTTP da recusa simulada (nulo = falha de transporte, sem resposta).</summary>
    public System.Net.HttpStatusCode? StatusDaFalha { get; set; }

    /// <summary>Quantas chamadas chegaram ao "provedor" (com ou sem falha).</summary>
    public int Chamadas { get; private set; }

    public Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken cancellationToken = default)
    {
        lock (Enviados)
        {
            Chamadas++;

            if (Falhar)
                throw new HttpRequestException("E-mail indisponível (simulado).", null, StatusDaFalha);

            Enviados.Add((destinatario, assunto, corpoHtml));
        }

        return Task.CompletedTask;
    }
}

public sealed class EspiaWhatsApp : IMensageriaWhatsApp
{
    public List<(TelefoneE164 Telefone, string Mensagem)> Enviados { get; } = [];

    /// <summary>
    /// O que o "provedor" devolve. Padrão: enviado, sem ID (como o Fake). Os testes do webhook trocam
    /// por um ID de mensagem pendente; os de indisponibilidade, por <see cref="ResultadoEnvioWhatsApp.Indisponivel"/>.
    /// Singleton — quem troca, restaura.
    /// </summary>
    public Func<ResultadoEnvioWhatsApp> Resultado { get; set; } = () => ResultadoEnvioWhatsApp.Enviado();

    /// <summary>Quantas chamadas chegaram ao "provedor" (aceitas ou não).</summary>
    public int Chamadas { get; private set; }

    public Task<ResultadoEnvioWhatsApp> EnviarAsync(TelefoneE164 telefone, string mensagem, CancellationToken cancellationToken = default)
    {
        var resultado = Resultado();
        lock (Enviados)
        {
            Chamadas++;
            if (resultado.Status != Plataforma.Dominio.Notificacoes.StatusCanal.Falhou)
                Enviados.Add((telefone, mensagem));
        }

        return Task.FromResult(resultado);
    }

    public void Restaurar() => Resultado = () => ResultadoEnvioWhatsApp.Enviado();
}
