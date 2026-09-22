using Plataforma.Dominio.Comum;

namespace Plataforma.Aplicacao.Notificacoes;

/// <summary>
/// Envio de WhatsApp (seção 4/9). No MVP, usado sempre para o código de confirmação e,
/// opcionalmente por negócio, para confirmações e lembretes. <c>Fake</c> em dev/testes;
/// <c>Oficial</c> (API Cloud da Meta) em produção. Provedores não oficiais são proibidos
/// como único canal do código — nunca entram aqui sem a flag <c>WhatsApp:PermitirNaoOficial</c>
/// (seção 4, ainda não implementada nesta fase).
/// </summary>
public interface IMensageriaWhatsApp
{
    Task EnviarAsync(TelefoneE164 telefone, string mensagem, CancellationToken cancellationToken = default);
}
