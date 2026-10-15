using System.Net;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;

namespace Plataforma.Infraestrutura.Notificacoes.Outbox;

public enum ClasseDespacho
{
    /// <summary>O provedor aceitou a mensagem.</summary>
    Aceito,

    /// <summary>Falha confirmada sem aceite: tentar de novo é seguro.</summary>
    FalhaRetentavel,

    /// <summary>Recusa que repetir não resolve.</summary>
    FalhaDefinitiva,

    /// <summary>Pode ter sido aceita (timeout, resposta perdida): nunca reenviar sozinha.</summary>
    Ambiguo,
}

/// <param name="Status">Estado do canal quando aceito (<see cref="StatusCanal.Pendente"/> = a confirmação vem pelo webhook).</param>
public sealed record ResultadoDespacho(ClasseDespacho Classe, string? IdMensagem = null, StatusCanal? Status = null);

/// <summary>
/// Um canal por vez, com o resultado já classificado para a outbox — sem lançar como contrato normal. É a abstração própria do despachante:
/// <see cref="INotificador"/> continua como está para quem envia direto (códigos, senhas, convites…).
/// </summary>
public sealed class DespachanteDeCanais
{
    private readonly IEmailSender _email;
    private readonly IMensageriaWhatsApp _whatsApp;
    private readonly ILogger<DespachanteDeCanais> _logger;

    public DespachanteDeCanais(IEmailSender email, IMensageriaWhatsApp whatsApp, ILogger<DespachanteDeCanais> logger)
    {
        _email = email;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    /// <summary>
    /// E-mail: a API do provedor não diz se uma falha de transporte aconteceu antes ou depois de aceitar a mensagem, então qualquer falha
    /// que não seja uma recusa clara (4xx, exceto 408/429) é retentável — o preço, numa queda rara depois do aceite, é um e-mail repetido
    /// (o Resend ainda não recebe uma chave de idempotência; está no backlog), nunca uma confirmação perdida.
    /// </summary>
    public async Task<ResultadoDespacho> EnviarEmailAsync(string destinatario, EmailMontado mensagem, CancellationToken cancellationToken)
    {
        try
        {
            await _email.EnviarAsync(destinatario, mensagem.Assunto, mensagem.CorpoHtml, cancellationToken);
            return new ResultadoDespacho(ClasseDespacho.Aceito, Status: StatusCanal.Enviado);
        }
        catch (Exception excecao) when (!cancellationToken.IsCancellationRequested)
        {
            // Só o tipo e o status HTTP: a mensagem da exceção pode carregar o endereço.
            var status = (excecao as HttpRequestException)?.StatusCode;
            _logger.LogWarning("Falha ao enviar e-mail pela outbox ({TipoErro}, HTTP {Status}).", excecao.GetType().Name, (int?)status);

            return status is { } codigo && (int)codigo is >= 400 and < 500 && codigo is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
                ? new ResultadoDespacho(ClasseDespacho.FalhaDefinitiva)
                : new ResultadoDespacho(ClasseDespacho.FalhaRetentavel);
        }
    }

    public async Task<ResultadoDespacho> EnviarWhatsAppAsync(TelefoneE164 telefone, string texto, CancellationToken cancellationToken)
    {
        ResultadoEnvioWhatsApp resultado;
        try
        {
            resultado = await _whatsApp.EnviarAsync(telefone, texto, cancellationToken);
        }
        catch (Exception excecao) when (!cancellationToken.IsCancellationRequested)
        {
            // O contrato do provedor é não lançar; se lançou, não dá para saber o que aconteceu.
            _logger.LogError("Provedor de WhatsApp lançou durante a outbox ({TipoErro}).", excecao.GetType().Name);
            return new ResultadoDespacho(ClasseDespacho.Ambiguo);
        }

        if (resultado.Status != StatusCanal.Falhou)
            return new ResultadoDespacho(ClasseDespacho.Aceito, resultado.IdMensagem, resultado.Status);

        return resultado.Falha switch
        {
            ClasseFalhaWhatsApp.Recusada => new ResultadoDespacho(ClasseDespacho.FalhaDefinitiva),
            ClasseFalhaWhatsApp.Ambigua => new ResultadoDespacho(ClasseDespacho.Ambiguo),
            _ => new ResultadoDespacho(ClasseDespacho.FalhaRetentavel),
        };
    }
}
