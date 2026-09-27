using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;

namespace Plataforma.Aplicacao.Notificacoes;

/// <summary>
/// Envio de WhatsApp (seção 4/9): código de confirmação e, opcionalmente por negócio,
/// confirmações, lembretes e o aviso ao profissional. <c>Fake</c> em dev/testes; <c>Oficial</c>
/// (API Cloud da Meta); <c>EvolutionApi</c> (não oficial, só com <c>WhatsApp:PermitirNaoOficial</c>
/// — viola os termos do WhatsApp e pode levar ao banimento do número, por isso nunca é o único
/// canal do código: o e-mail sai em paralelo).
/// </summary>
public interface IMensageriaWhatsApp
{
    /// <summary>
    /// Nunca lança por falha do provedor (rede, timeout, recusa): devolve
    /// <see cref="ResultadoEnvioWhatsApp.Indisponivel"/> — um canal não pode derrubar o outro (seção 8.1).
    /// </summary>
    Task<ResultadoEnvioWhatsApp> EnviarAsync(TelefoneE164 telefone, string mensagem, CancellationToken cancellationToken = default);
}

/// <param name="Status">
/// <see cref="StatusCanal.Pendente"/> quando o provedor aceitou e a confirmação chega depois pelo
/// webhook; <see cref="StatusCanal.Enviado"/> quando o provedor não tem webhook de status.
/// </param>
/// <param name="IdMensagem">ID da mensagem no provedor, para o webhook achar o registro de origem.</param>
public sealed record ResultadoEnvioWhatsApp(StatusCanal Status, string? IdMensagem = null)
{
    public static ResultadoEnvioWhatsApp Enviado(string? idMensagem = null) => new(StatusCanal.Enviado, idMensagem);

    public static ResultadoEnvioWhatsApp AguardandoConfirmacao(string idMensagem) => new(StatusCanal.Pendente, idMensagem);

    public static ResultadoEnvioWhatsApp Indisponivel() => new(StatusCanal.Falhou);
}
