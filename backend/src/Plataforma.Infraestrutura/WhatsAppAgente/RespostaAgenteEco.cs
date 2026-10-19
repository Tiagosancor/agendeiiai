using Plataforma.Aplicacao.WhatsAppAgente;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Repete o que o cliente disse (Fase 1). Continua sendo o padrão (<c>Ai__Provider=Eco</c>), sem chave nem custo; com
/// <c>Ai__Provider=Gemini</c> a fábrica de DI entrega o <see cref="OrquestradorAgente"/> no lugar.
/// </summary>
public sealed class RespostaAgenteEco : IRespostaAgente
{
    public Task<string?> ResponderAsync(ContextoRespostaAgente contexto, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>($"Recebi: {contexto.TextoRecebido}");
}
