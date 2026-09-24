using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Assinaturas;

/// <summary>
/// Webhook de pagamento (seção 7/8.6.6): só provedor que aceita webhook, só com assinatura
/// válida, e idempotente por ID do evento — registrar o evento e aplicar o efeito acontecem na
/// mesma transação, então uma falha no meio deixa o evento sem registro e o provedor reenvia.
/// </summary>
public sealed class ProcessadorWebhookPagamento : IProcessadorWebhookPagamento
{
    private readonly IEnumerable<IGatewayPagamento> _gateways;
    private readonly PlataformaDbContext _dbContext;
    private readonly ILogger<ProcessadorWebhookPagamento> _logger;

    public ProcessadorWebhookPagamento(
        IEnumerable<IGatewayPagamento> gateways, PlataformaDbContext dbContext, ILogger<ProcessadorWebhookPagamento> logger)
    {
        _gateways = gateways;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ResultadoWebhook> ProcessarAsync(string provedor, RequisicaoWebhook requisicao, CancellationToken cancellationToken = default)
    {
        var gateway = _gateways.FirstOrDefault(g => g.AceitaWebhook && string.Equals(g.Provedor, provedor, StringComparison.OrdinalIgnoreCase));
        if (gateway is null)
            return ResultadoWebhook.ProvedorDesconhecido;

        var evento = await gateway.InterpretarWebhookAsync(requisicao, cancellationToken);
        if (evento is null)
            return ResultadoWebhook.AssinaturaInvalida;

        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            _dbContext.EventosWebhookPagamento.Add(new EventoWebhookPagamento(gateway.Provedor, evento.IdEvento, evento.Tipo.ToString()));

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
            {
                _logger.LogInformation("Webhook {Provedor}/{IdEvento} repetido — ignorado.", gateway.Provedor, evento.IdEvento);
                return ResultadoWebhook.Repetido;
            }

            await AplicarAsync(gateway.Provedor, evento, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            return ResultadoWebhook.Processado;
        });
    }

    private async Task AplicarAsync(string provedor, EventoPagamentoGateway evento, CancellationToken cancellationToken)
    {
        if (evento.Tipo == TipoEventoPagamento.Ignorado || evento.IdExternoAssinatura is null)
            return;

        // Webhook não tem tenant: busca explícita pelo vínculo com o gateway.
        var assinatura = await _dbContext.Assinaturas.IgnoreQueryFilters()
            .Include(a => a.Historico)
            .FirstOrDefaultAsync(a => a.ProvedorGateway == provedor && a.IdExternoGateway == evento.IdExternoAssinatura, cancellationToken);

        if (assinatura is null)
        {
            _logger.LogWarning("Webhook {Provedor}/{IdEvento} para assinatura desconhecida.", provedor, evento.IdEvento);
            return;
        }

        var autor = $"gateway:{provedor}";
        var agora = DateTimeOffset.UtcNow;

        switch (evento.Tipo)
        {
            case TipoEventoPagamento.PagamentoConfirmado
                when evento is { Valor: decimal valor, PagoEm: DateTimeOffset pagoEm, PeriodoInicio: DateTimeOffset inicio, PeriodoFim: DateTimeOffset fim }:
                var cobranca = ServicoAssinatura.RegistrarPagamento(
                    assinatura, valor, FormaCobranca.Outro, pagoEm, inicio, fim, OrigemCobranca.Gateway, evento.IdExternoCobranca, autor, agora);
                _dbContext.CobrancasAssinatura.Add(cobranca);
                break;

            case TipoEventoPagamento.AssinaturaCancelada when assinatura.Estado != EstadoAssinatura.Cancelada:
                ServicoAssinatura.Cancelar(assinatura, "Cancelada no gateway", autor, agora);
                break;

            default:
                _logger.LogWarning("Webhook {Provedor}/{IdEvento} ({Tipo}) sem dados suficientes — ignorado.", provedor, evento.IdEvento, evento.Tipo);
                break;
        }
    }
}
