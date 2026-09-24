using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Assinaturas;

/// <summary>Pagamento combinado por fora (PIX/WhatsApp) e registrado à mão na administração da plataforma.</summary>
public sealed class GatewayPagamentoManual : IGatewayPagamento
{
    private readonly OpcoesCobranca _opcoes;

    public GatewayPagamentoManual(IOptions<OpcoesCobranca> opcoes) => _opcoes = opcoes.Value;

    public string Provedor => "manual";

    public bool AceitaWebhook => false;

    public Task<string?> CriarClienteAsync(DadosClienteGateway dados, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> CriarOuAlterarAssinaturaAsync(DadosAssinaturaGateway dados, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task CancelarAssinaturaAsync(string? idExternoAssinatura, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<InstrucoesPagamento> GerarLinkPagamentoAsync(DadosCobrancaGateway dados, CancellationToken cancellationToken = default)
    {
        var texto = $"Plano {dados.NomePlano} ({dados.Periodicidade.ToLowerInvariant()}): R$ {dados.Valor:F2}. " +
            "Faça o PIX e mande o comprovante pelo WhatsApp — a liberação é feita na hora.";

        return Task.FromResult(new InstrucoesPagamento(null, _opcoes.ChavePix, _opcoes.WhatsAppContato, texto));
    }

    public Task<EventoPagamentoGateway?> InterpretarWebhookAsync(RequisicaoWebhook requisicao, CancellationToken cancellationToken = default) =>
        Task.FromResult<EventoPagamentoGateway?>(null);
}
