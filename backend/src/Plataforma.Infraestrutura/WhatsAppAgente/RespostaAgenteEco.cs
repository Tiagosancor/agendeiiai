using Plataforma.Aplicacao.WhatsAppAgente;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// TEMPORÁRIO (Fase 1): repete o que o cliente disse, só para validar o fluxo ponta a ponta. A Fase 2 troca esta
/// implementação de <see cref="IRespostaAgente"/> pelo orquestrador de IA — nada mais precisa mudar.
/// </summary>
public sealed class RespostaAgenteEco : IRespostaAgente
{
    public Task<string?> ResponderAsync(ContextoRespostaAgente contexto, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>($"Recebi: {contexto.TextoRecebido}");
}
