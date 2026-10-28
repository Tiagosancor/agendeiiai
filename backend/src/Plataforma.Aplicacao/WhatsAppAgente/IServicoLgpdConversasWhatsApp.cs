using Plataforma.Dominio.Comum;

namespace Plataforma.Aplicacao.WhatsAppAgente;

/// <summary>Mensagem exportada de uma conversa. <c>Papel</c>: cliente | assistente | sistema (nunca ferramenta nem nota de contexto).</summary>
public sealed record ExportacaoMensagemWhatsApp(DateTimeOffset Em, string Papel, string Texto);

public sealed record ExportacaoConversaWhatsApp(DateTimeOffset UltimaMensagemEm, IReadOnlyList<ExportacaoMensagemWhatsApp> Mensagens);

/// <summary>
/// LGPD das conversas do agente de WhatsApp (G11). O negócio é sempre o do contexto da requisição. Nada aqui registra dado pessoal em log.
/// </summary>
public interface IServicoLgpdConversasWhatsApp
{
    /// <summary>
    /// Apaga as mensagens das conversas do cliente (por <c>ClienteId</c> ou pelo telefone, nas duas formas do 9º dígito) e anonimiza
    /// as linhas. NÃO abre transação: roda dentro da do chamador (a anonimização do cliente, que já tem a linha do cliente travada).
    /// Devolve quantas conversas foram anonimizadas.
    /// </summary>
    Task<int> AnonimizarDoClienteAsync(Guid clienteId, TelefoneE164? telefone, CancellationToken cancellationToken = default);

    /// <summary>"Apagar conversa": mesma regra para uma conversa só (atende quem conversou sem virar cliente). Falso se não existe neste negócio.</summary>
    Task<bool> ApagarConversaAsync(Guid conversaId, CancellationToken cancellationToken = default);

    /// <summary>Conversas do cliente para a exportação (LGPD). Lido antes de anonimizar.</summary>
    Task<IReadOnlyList<ExportacaoConversaWhatsApp>> ExportarDoClienteAsync(Guid clienteId, TelefoneE164? telefone, CancellationToken cancellationToken = default);
}
