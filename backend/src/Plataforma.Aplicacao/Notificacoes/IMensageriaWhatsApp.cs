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
/// <param name="Falha">
/// Só quando <paramref name="Status"/> é <see cref="StatusCanal.Falhou"/>: o que a falha permite concluir (usado pela outbox para decidir
/// entre nova tentativa, falha definitiva e "desconhecida"). Os consumidores diretos só olham o <paramref name="Status"/>.
/// </param>
public sealed record ResultadoEnvioWhatsApp(StatusCanal Status, string? IdMensagem = null, ClasseFalhaWhatsApp Falha = ClasseFalhaWhatsApp.Nenhuma)
{
    public static ResultadoEnvioWhatsApp Enviado(string? idMensagem = null) => new(StatusCanal.Enviado, idMensagem);

    public static ResultadoEnvioWhatsApp AguardandoConfirmacao(string idMensagem) => new(StatusCanal.Pendente, idMensagem);

    /// <summary>Falha confirmada ANTES do aceite (conexão recusada, 5xx, indisponível): nada foi enviado, tentar de novo é seguro.</summary>
    public static ResultadoEnvioWhatsApp Indisponivel() => new(StatusCanal.Falhou, null, ClasseFalhaWhatsApp.NaoAceita);

    /// <summary>Recusa definitiva do provedor (ex.: número sem WhatsApp, 4xx): repetir não adianta.</summary>
    public static ResultadoEnvioWhatsApp Recusada() => new(StatusCanal.Falhou, null, ClasseFalhaWhatsApp.Recusada);

    /// <summary>Timeout ou resposta perdida depois de a requisição poder ter chegado: a mensagem talvez tenha sido aceita.</summary>
    public static ResultadoEnvioWhatsApp Ambigua() => new(StatusCanal.Falhou, null, ClasseFalhaWhatsApp.Ambigua);
}

public enum ClasseFalhaWhatsApp
{
    Nenhuma = 0,

    /// <summary>Não aceita, com certeza: retentável.</summary>
    NaoAceita = 1,

    /// <summary>Recusa definitiva: não retentável.</summary>
    Recusada = 2,

    /// <summary>Pode ter sido aceita: nunca reenviar sozinha.</summary>
    Ambigua = 3,
}
