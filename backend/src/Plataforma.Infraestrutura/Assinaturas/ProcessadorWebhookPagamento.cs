using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Assinaturas;

/// <summary>
/// Webhook de pagamento (seção 7/8.6.6): só provedor que aceita webhook, só com assinatura
/// válida, e idempotente por ID do evento — registrar o evento (com o corpo bruto) e aplicar o efeito
/// acontecem na mesma transação, então uma falha no meio deixa o evento sem registro e o provedor
/// reenvia. Evento que não muda nada (desconhecido, de outra assinatura, pagamento já registrado)
/// fica gravado com o <c>Resultado</c> e responde 200 — só erro de verdade faz o provedor repetir.
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

            var registro = new EventoWebhookPagamento(gateway.Provedor, evento.IdEvento, evento.Nome ?? evento.Tipo.ToString(), requisicao.Corpo);
            _dbContext.EventosWebhookPagamento.Add(registro);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
            {
                _logger.LogInformation("Webhook {Provedor}/{IdEvento} repetido — ignorado.", gateway.Provedor, evento.IdEvento);
                return ResultadoWebhook.Repetido;
            }

            registro.RegistrarResultado(await AplicarAsync(gateway.Provedor, evento, cancellationToken));
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            return ResultadoWebhook.Processado;
        });
    }

    private async Task<string> AplicarAsync(string provedor, EventoPagamentoGateway evento, CancellationToken cancellationToken)
    {
        if (evento.Tipo == TipoEventoPagamento.Ignorado)
            return "Ignorado";
        if (evento.IdExternoAssinatura is null)
            return "SemAssinatura";

        // Webhook não tem tenant: busca explícita pelo vínculo com o gateway.
        var assinatura = await _dbContext.Assinaturas.IgnoreQueryFilters()
            .Include(a => a.Historico)
            .FirstOrDefaultAsync(a => a.ProvedorGateway == provedor && a.IdExternoGateway == evento.IdExternoAssinatura, cancellationToken);

        if (assinatura is null)
        {
            // Inclui a assinatura antiga de quem contratou de novo (o gateway avisa quando ela é removida).
            _logger.LogWarning("Webhook {Provedor}/{IdEvento} para assinatura desconhecida.", provedor, evento.IdEvento);
            return "AssinaturaDesconhecida";
        }

        var autor = $"gateway:{provedor}";
        var agora = DateTimeOffset.UtcNow;

        try
        {
            switch (evento.Tipo)
            {
                case TipoEventoPagamento.PagamentoConfirmado when evento is { Valor: decimal valor, PagoEm: DateTimeOffset pagoEm }:
                {
                    // Cartão manda "confirmado" e depois "recebido" da mesma cobrança: registra uma vez só.
                    if (evento.IdExternoCobranca is { } idCobranca
                        && await _dbContext.CobrancasAssinatura.IgnoreQueryFilters()
                            .AnyAsync(c => c.Origem == OrigemCobranca.Gateway && c.IdExterno == idCobranca, cancellationToken))
                        return "JaRegistrado";

                    var inicio = evento.PeriodoInicio ?? evento.Vencimento ?? pagoEm;
                    var fim = evento.PeriodoFim ?? FimDoPeriodo(inicio, assinatura.Periodicidade);
                    var forma = Enum.TryParse<FormaCobranca>(evento.Forma, out var f) ? f : FormaCobranca.Outro;
                    _dbContext.CobrancasAssinatura.Add(ServicoAssinatura.RegistrarPagamento(
                        assinatura, valor, forma, pagoEm, inicio, fim, OrigemCobranca.Gateway, evento.IdExternoCobranca, autor, agora));
                    return "Aplicado";
                }

                case TipoEventoPagamento.CobrancaVencida:
                {
                    var vencimento = evento.Vencimento ?? agora;
                    // Vencida de um período que já está coberto (pago por outro caminho): nada a fazer.
                    if (assinatura.PrazoAtual is DateTimeOffset prazo && prazo > vencimento.AddDays(1))
                        return "PeriodoJaCoberto";
                    return ServicoAssinatura.MarcarCobrancaVencida(assinatura, vencimento, autor, agora) ? "Aplicado" : "SemMudanca";
                }

                case TipoEventoPagamento.PagamentoEstornado:
                {
                    var cobranca = evento.IdExternoCobranca is { } idCobranca
                        ? await _dbContext.CobrancasAssinatura.IgnoreQueryFilters()
                            .FirstOrDefaultAsync(c => c.Origem == OrigemCobranca.Gateway && c.IdExterno == idCobranca, cancellationToken)
                        : null;
                    if (cobranca is null)
                        return "CobrancaDesconhecida";
                    ServicoAssinatura.EstornarPagamento(assinatura, cobranca, autor, agora);
                    return "Aplicado";
                }

                case TipoEventoPagamento.AssinaturaCancelada:
                    if (assinatura.Estado == EstadoAssinatura.Cancelada || assinatura.CancelamentoAgendado)
                        return "JaCancelada";
                    // Removida no painel do gateway: mesma regra do cancelamento pelo negócio — usa até o fim do período pago.
                    ServicoAssinatura.PedirCancelamento(assinatura, autor, agora);
                    return "Aplicado";

                default:
                    _logger.LogWarning("Webhook {Provedor}/{IdEvento} ({Tipo}) sem dados suficientes — ignorado.", provedor, evento.IdEvento, evento.Tipo);
                    return "DadosInsuficientes";
            }
        }
        catch (RegraAssinaturaException excecao)
        {
            // Ex.: pagamento de assinatura já cancelada — registra e segue; repetir não mudaria nada.
            _logger.LogWarning("Webhook {Provedor}/{IdEvento} recusado pela regra da assinatura: {Motivo}", provedor, evento.IdEvento, excecao.Message);
            return "Recusado";
        }
    }

    /// <summary>
    /// Um período a partir do vencimento pago: vale até o fim do dia (horário de Brasília) do próximo vencimento — a
    /// cobrança seguinte ainda pode ser paga nesse dia sem cair em carência.
    /// </summary>
    internal static DateTimeOffset FimDoPeriodo(DateTimeOffset inicio, Periodicidade periodicidade)
    {
        var local = inicio.ToOffset(TimeSpan.FromHours(-3));
        var proximo = periodicidade == Periodicidade.Anual ? local.AddYears(1) : local.AddMonths(1);
        return new DateTimeOffset(proximo.Date, proximo.Offset).AddDays(1).AddSeconds(-1).ToUniversalTime();
    }
}
