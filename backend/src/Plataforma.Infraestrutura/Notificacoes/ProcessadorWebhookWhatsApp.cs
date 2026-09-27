using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Notificacoes;

/// <summary>
/// Webhook de status do Evolution API. Formato (v2): <c>{ "event": "messages.update", "data": {
/// "keyId": "...", "status": "DELIVERY_ACK", "fromMe": true } }</c> — <c>data</c> pode vir como
/// lista, e versões antigas mandam <c>{ "key": { "id" }, "update": { "status": 3 } }</c>; os dois
/// formatos são aceitos. Sem tenant: o registro de origem é achado só pelo ID da mensagem
/// (<c>IgnoreQueryFilters</c>, como o webhook de pagamento).
/// </summary>
public sealed class ProcessadorWebhookWhatsApp : IProcessadorWebhookWhatsApp
{
    public const string Provedor = "evolution";

    private readonly PlataformaDbContext _dbContext;
    private readonly OpcoesWhatsApp _opcoes;
    private readonly ILogger<ProcessadorWebhookWhatsApp> _logger;

    public ProcessadorWebhookWhatsApp(PlataformaDbContext dbContext, IOptions<OpcoesWhatsApp> opcoes, ILogger<ProcessadorWebhookWhatsApp> logger)
    {
        _dbContext = dbContext;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public async Task<ResultadoWebhookWhatsApp> ProcessarAsync(string? token, string corpo, CancellationToken cancellationToken = default)
    {
        var segredo = _opcoes.Evolution.SegredoWebhook;
        if (_opcoes.Provedor != ProvedorWhatsApp.EvolutionApi || string.IsNullOrWhiteSpace(segredo))
            return ResultadoWebhookWhatsApp.NaoConfigurado;

        if (!TokenConfere(token, segredo))
            return ResultadoWebhookWhatsApp.TokenInvalido;

        List<(string IdMensagem, StatusCanal Status)> eventos;
        try
        {
            eventos = LerEventos(corpo);
        }
        catch (JsonException)
        {
            return ResultadoWebhookWhatsApp.CorpoInvalido;
        }

        if (eventos.Count == 0)
            return ResultadoWebhookWhatsApp.Ignorado;

        var algumNovo = false;
        foreach (var (idMensagem, status) in eventos)
            algumNovo |= await AplicarAsync(idMensagem, status, cancellationToken);

        return algumNovo ? ResultadoWebhookWhatsApp.Processado : ResultadoWebhookWhatsApp.Repetido;
    }

    /// <summary>Grava o evento (o índice único recusa o repetido) e atualiza o registro de origem, na mesma transação.</summary>
    private async Task<bool> AplicarAsync(string idMensagem, StatusCanal status, CancellationToken cancellationToken)
    {
        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            _dbContext.EventosWebhookWhatsApp.Add(new EventoWebhookWhatsApp(Provedor, idMensagem, status));
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
            {
                return false;
            }

            var codigos = await _dbContext.CodigosVerificacao.IgnoreQueryFilters()
                .Where(c => c.IdMensagemWhatsApp == idMensagem)
                .ToListAsync(cancellationToken);
            foreach (var codigo in codigos)
                codigo.AtualizarStatusWhatsApp(status);

            var avisos = await _dbContext.NotificacoesProfissional.IgnoreQueryFilters()
                .Where(n => n.IdMensagemWhatsApp == idMensagem)
                .ToListAsync(cancellationToken);
            foreach (var aviso in avisos)
                aviso.AtualizarStatusWhatsApp(status);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            if (codigos.Count == 0 && avisos.Count == 0)
                _logger.LogInformation("Status {Status} do WhatsApp guardado para uma mensagem ainda sem registro de origem.", status);

            return true;
        });
    }

    /// <summary>Compara em tempo constante (hash dos dois lados, para o tamanho também não vazar).</summary>
    private static bool TokenConfere(string? token, string segredo)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        var recebido = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var esperado = SHA256.HashData(Encoding.UTF8.GetBytes(segredo));
        return CryptographicOperations.FixedTimeEquals(recebido, esperado);
    }

    public static List<(string IdMensagem, StatusCanal Status)> LerEventos(string corpo)
    {
        using var documento = JsonDocument.Parse(corpo);
        var raiz = documento.RootElement;
        var resultado = new List<(string, StatusCanal)>();

        if (raiz.ValueKind != JsonValueKind.Object)
            return resultado;

        var evento = raiz.TryGetProperty("event", out var nome) && nome.ValueKind == JsonValueKind.String
            ? nome.GetString()!.ToLowerInvariant().Replace('_', '.')
            : "";
        if (evento is not ("messages.update" or "send.message"))
            return resultado;

        if (!raiz.TryGetProperty("data", out var dados))
            return resultado;

        var itens = dados.ValueKind == JsonValueKind.Array ? dados.EnumerateArray().ToList() : [dados];
        foreach (var item in itens)
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            item.TryGetProperty("key", out var chave);
            var temChave = chave.ValueKind == JsonValueKind.Object;

            // Só as mensagens que nós mandamos têm registro de origem.
            if (EhFalso(item, "fromMe") || (temChave && EhFalso(chave, "fromMe")))
                continue;

            var id = Texto(item, "keyId") ?? (temChave ? Texto(chave, "id") : null);

            StatusCanal? status = null;
            if (item.TryGetProperty("status", out var valor))
                status = MapearStatus(valor);
            else if (item.TryGetProperty("update", out var atualizacao) && atualizacao.ValueKind == JsonValueKind.Object
                && atualizacao.TryGetProperty("status", out var valorAntigo))
                status = MapearStatus(valorAntigo);

            if (!string.IsNullOrWhiteSpace(id) && status is not null)
                resultado.Add((id, status.Value));
        }

        return resultado;
    }

    /// <summary>
    /// Baileys: 0 ERROR, 1 PENDING, 2 SERVER_ACK, 3 DELIVERY_ACK, 4 READ, 5 PLAYED. "Pendente" não
    /// muda nada (é o estado logo depois do envio); do servidor em diante, a mensagem saiu.
    /// </summary>
    private static StatusCanal? MapearStatus(JsonElement valor) => valor.ValueKind switch
    {
        JsonValueKind.Number when valor.TryGetInt32(out var numero) => numero switch
        {
            0 => StatusCanal.Falhou,
            >= 2 and <= 5 => StatusCanal.Enviado,
            _ => null,
        },
        JsonValueKind.String => valor.GetString()!.ToUpperInvariant() switch
        {
            "ERROR" or "FAILED" => StatusCanal.Falhou,
            "SERVER_ACK" or "DELIVERY_ACK" or "READ" or "PLAYED" or "SENT" or "DELIVERED" => StatusCanal.Enviado,
            _ => null,
        },
        _ => null,
    };

    private static bool EhFalso(JsonElement elemento, string propriedade) =>
        elemento.TryGetProperty(propriedade, out var valor) && valor.ValueKind == JsonValueKind.False;

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.TryGetProperty(propriedade, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;
}
