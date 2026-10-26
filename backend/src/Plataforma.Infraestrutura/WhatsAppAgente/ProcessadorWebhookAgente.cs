using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Webhook por instância da Evolution (<c>MESSAGES_UPSERT</c>, <c>QRCODE_UPDATED</c>, <c>CONNECTION_UPDATE</c>).
/// Formato v2: <c>{ "event": "MESSAGES_UPSERT", "instance": "nome", "data": { "key": { "remoteJid", "fromMe", "id" },
/// "message": { "conversation" }, "messageType" } }</c>. O negócio vem SEMPRE da instância gravada no banco
/// (<c>IgnoreQueryFilters</c> só nessa busca, como o webhook de pagamento) — nunca do corpo. Instância
/// desconhecida: loga o evento e ignora. Nunca loga o conteúdo das mensagens nem o telefone.
/// </summary>
public sealed class ProcessadorWebhookAgente : IProcessadorWebhookAgente
{
    private const string SufixoIndividual = "@s.whatsapp.net";

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IDeduplicadorMensagens _deduplicador;
    private readonly IFilaRespostaAgente _fila;
    private readonly OpcoesAgenteWhatsApp _opcoes;
    private readonly ILogger<ProcessadorWebhookAgente> _logger;

    public ProcessadorWebhookAgente(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IDeduplicadorMensagens deduplicador,
        IFilaRespostaAgente fila, IOptions<OpcoesAgenteWhatsApp> opcoes,
        ILogger<ProcessadorWebhookAgente> logger)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _deduplicador = deduplicador;
        _fila = fila;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public async Task<ResultadoWebhookAgente> ProcessarAsync(string? segredo, string corpo, CancellationToken cancellationToken = default)
    {
        if (!_opcoes.WebhookConfigurado)
            return ResultadoWebhookAgente.NaoConfigurado;

        if (!SegredoConfere(segredo, _opcoes.WebhookSecret!))
            return ResultadoWebhookAgente.SegredoInvalido;

        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(corpo);
        }
        catch (JsonException)
        {
            return ResultadoWebhookAgente.CorpoInvalido;
        }

        using (documento)
        {
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
                return ResultadoWebhookAgente.CorpoInvalido;

            var evento = Texto(raiz, "event")?.Trim().ToUpperInvariant().Replace('.', '_');
            var nomeInstancia = Texto(raiz, "instance");
            if (string.IsNullOrEmpty(evento) || string.IsNullOrWhiteSpace(nomeInstancia))
                return ResultadoWebhookAgente.Ignorado;

            return evento switch
            {
                "MESSAGES_UPSERT" => await ProcessarMensagensAsync(raiz, nomeInstancia, cancellationToken),
                "QRCODE_UPDATED" => await AtualizarConexaoAsync(nomeInstancia, StatusInstanciaWhatsApp.AguardandoQrCode, null, cancellationToken),
                "CONNECTION_UPDATE" => await ProcessarConexaoAsync(raiz, nomeInstancia, cancellationToken),
                _ => ResultadoWebhookAgente.Ignorado,
            };
        }
    }

    // ---------------------------------------------------------------- mensagens

    private async Task<ResultadoWebhookAgente> ProcessarMensagensAsync(JsonElement raiz, string nomeInstancia, CancellationToken cancellationToken)
    {
        if (!raiz.TryGetProperty("data", out var dados))
            return ResultadoWebhookAgente.Ignorado;

        var itens = dados.ValueKind == JsonValueKind.Array ? dados.EnumerateArray().ToList() : [dados];

        var algumNovo = false;
        var algumRepetido = false;
        foreach (var item in itens)
        {
            var recebida = LerMensagemRecebida(item);
            if (recebida is null)
                continue;

            switch (await ProcessarMensagemAsync(nomeInstancia, recebida, cancellationToken))
            {
                case ResultadoWebhookAgente.Processado: algumNovo = true; break;
                case ResultadoWebhookAgente.Repetido: algumRepetido = true; break;
            }
        }

        return algumNovo ? ResultadoWebhookAgente.Processado : algumRepetido ? ResultadoWebhookAgente.Repetido : ResultadoWebhookAgente.Ignorado;
    }

    private sealed record MensagemRecebida(string IdExterno, string TelefoneE164, string Texto);

    /// <summary>Só texto novo, de cliente individual, que não foi enviado por nós. Grupos, difusão, mídia e <c>fromMe</c> → <c>null</c>.</summary>
    private static MensagemRecebida? LerMensagemRecebida(JsonElement dados)
    {
        if (dados.ValueKind != JsonValueKind.Object || !dados.TryGetProperty("key", out var chave) || chave.ValueKind != JsonValueKind.Object)
            return null;

        if (chave.TryGetProperty("fromMe", out var deMim) && deMim.ValueKind == JsonValueKind.True)
            return null;

        var id = Texto(chave, "id");
        var jid = JidDoTelefone(chave);
        // Grupos (@g.us), difusão e "@lid" sem telefone conhecido ficam de fora de propósito.
        if (string.IsNullOrWhiteSpace(id) || jid is null)
            return null;

        var tipo = Texto(dados, "messageType");
        if (tipo is not null && tipo is not ("conversation" or "extendedTextMessage"))
            return null;

        string? texto = null;
        if (dados.TryGetProperty("message", out var mensagem) && mensagem.ValueKind == JsonValueKind.Object)
        {
            texto = Texto(mensagem, "conversation");
            if (string.IsNullOrWhiteSpace(texto) && mensagem.TryGetProperty("extendedTextMessage", out var estendida) && estendida.ValueKind == JsonValueKind.Object)
                texto = Texto(estendida, "text");
        }

        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var digitos = jid[..^SufixoIndividual.Length];
        if (!TelefoneE164.TentarCriar("+" + digitos, out var telefone))
            return null;

        return new MensagemRecebida(id.Trim(), telefone!.Valor, texto);
    }

    /// <summary>
    /// JID com o telefone do cliente (<c>5571...@s.whatsapp.net</c>). O WhatsApp novo identifica o remetente por um
    /// "@lid" (id opaco, sem telefone) em <c>remoteJid</c> e manda o telefone real em <c>senderPn</c> (v2.3.0, confirmado
    /// no banco da Evolution) ou <c>remoteJidAlt</c> (Baileys mais novo). Sem nenhum dos dois, ignora: não há telefone.
    /// </summary>
    public static string? JidDoTelefone(JsonElement chave)
    {
        // Grupo, difusão, status e canal (newsletter) nunca são um cliente: descarta ANTES de olhar senderPn/remoteJidAlt,
        // que num grupo trazem o telefone de um participante (plano da Fase 3, E5).
        var remoto = Texto(chave, "remoteJid");
        if (remoto is not null && (remoto.EndsWith("@g.us", StringComparison.Ordinal)
            || remoto.EndsWith("@broadcast", StringComparison.Ordinal)
            || remoto.EndsWith("@newsletter", StringComparison.Ordinal)))
            return null;

        foreach (var campo in new[] { "remoteJid", "senderPn", "remoteJidAlt" })
        {
            var valor = Texto(chave, campo);
            if (valor is null || !valor.EndsWith(SufixoIndividual, StringComparison.Ordinal))
                continue;

            // "5571...:12@s.whatsapp.net" = aparelho vinculado; o telefone é o que vem antes do ":".
            var digitos = valor[..^SufixoIndividual.Length].Split(':')[0];
            return digitos + SufixoIndividual;
        }

        return null;
    }

    private async Task<ResultadoWebhookAgente> ProcessarMensagemAsync(string nomeInstancia, MensagemRecebida recebida, CancellationToken cancellationToken)
    {
        var instancia = await BuscarInstanciaAsync(nomeInstancia, rastrear: false, cancellationToken);
        if (instancia is null)
        {
            _logger.LogWarning("Webhook do agente: instância desconhecida ({Instancia}); evento ignorado.", nomeInstancia);
            return ResultadoWebhookAgente.Ignorado;
        }

        // A partir daqui o negócio é o da instância gravada (nunca o do corpo).
        _contextoNegocio.Definir(instancia.NegocioId);

        if (!await _deduplicador.RegistrarSeNovaAsync(instancia.NomeInstancia, recebida.IdExterno, cancellationToken))
            return ResultadoWebhookAgente.Repetido;

        var salvo = await SalvarRecebidaAsync(instancia, recebida, cancellationToken);
        if (salvo is null)
            return ResultadoWebhookAgente.Repetido;

        if (instancia.AgenteAtivo && salvo.AgenteResponde)
            await EnfileirarAsync(salvo, cancellationToken);

        return ResultadoWebhookAgente.Processado;
    }

    /// <summary>Acha ou cria a conversa e grava a mensagem. <c>null</c> = a mensagem já existia (índice único por instância).</summary>
    private async Task<ConversaWhatsApp?> SalvarRecebidaAsync(InstanciaWhatsApp instancia, MensagemRecebida recebida, CancellationToken cancellationToken)
    {
        for (var tentativa = 1; tentativa <= 2; tentativa++)
        {
            var agora = DateTimeOffset.UtcNow;
            var conversa = await _dbContext.ConversasWhatsApp
                .FirstOrDefaultAsync(c => c.InstanciaId == instancia.Id && c.TelefoneCliente == recebida.TelefoneE164, cancellationToken);

            if (conversa is null)
            {
                conversa = new ConversaWhatsApp(instancia.NegocioId, instancia.Id, recebida.TelefoneE164, agora);
                _dbContext.ConversasWhatsApp.Add(conversa);
            }
            else
            {
                conversa.RegistrarMensagem(agora);
            }

            _dbContext.MensagensWhatsApp.Add(new MensagemWhatsApp(
                instancia.NegocioId, instancia.Id, conversa.Id, recebida.IdExterno, PapelMensagemWhatsApp.Usuario, recebida.Texto));

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return conversa;
            }
            catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
            {
                _dbContext.ChangeTracker.Clear();

                if (await _dbContext.MensagensWhatsApp.AnyAsync(m => m.InstanciaId == instancia.Id && m.IdExterno == recebida.IdExterno, cancellationToken))
                    return null;

                // Senão foi a corrida de duas mensagens criando a mesma conversa: a segunda tentativa a encontra.
                if (tentativa == 2)
                    throw;
            }
        }

        return null;
    }

    /// <summary>
    /// A resposta (eco ou IA) NÃO é produzida na requisição: o webhook só enfileira o ID da conversa e responde 200. Falha ao
    /// enfileirar nunca derruba o webhook — a mensagem já está salva e a varredura de recuperação a encontra.
    /// </summary>
    private async Task EnfileirarAsync(ConversaWhatsApp conversa, CancellationToken cancellationToken)
    {
        try
        {
            await _fila.EnfileirarAsync(conversa.Id, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            _logger.LogWarning("Webhook do agente: falha ao enfileirar (conversa {ConversaId}): {Tipo}.", conversa.Id, excecao.GetType().Name);
        }
    }

    // ---------------------------------------------------------------- conexão

    private async Task<ResultadoWebhookAgente> ProcessarConexaoAsync(JsonElement raiz, string nomeInstancia, CancellationToken cancellationToken)
    {
        var estado = raiz.TryGetProperty("data", out var dados)
            ? Texto(dados.ValueKind == JsonValueKind.Array && dados.GetArrayLength() > 0 ? dados[0] : dados, "state")
            : null;

        var status = estado?.ToLowerInvariant() switch
        {
            "open" => StatusInstanciaWhatsApp.Conectada,
            "connecting" => StatusInstanciaWhatsApp.AguardandoQrCode,
            "close" => StatusInstanciaWhatsApp.Desconectada,
            _ => (StatusInstanciaWhatsApp?)null,
        };

        if (status is null)
            return ResultadoWebhookAgente.Ignorado;

        // O número conectado vem no "sender" de topo (jid da própria instância), quando vem.
        var sender = Texto(raiz, "sender");
        var telefone = sender is not null && sender.EndsWith(SufixoIndividual, StringComparison.Ordinal)
            ? sender[..^SufixoIndividual.Length]
            : null;

        return await AtualizarConexaoAsync(nomeInstancia, status.Value, telefone, cancellationToken);
    }

    private async Task<ResultadoWebhookAgente> AtualizarConexaoAsync(
        string nomeInstancia, StatusInstanciaWhatsApp status, string? telefone, CancellationToken cancellationToken)
    {
        var instancia = await BuscarInstanciaAsync(nomeInstancia, rastrear: true, cancellationToken);
        if (instancia is null)
        {
            _logger.LogWarning("Webhook do agente: instância desconhecida ({Instancia}); evento de conexão ignorado.", nomeInstancia);
            return ResultadoWebhookAgente.Ignorado;
        }

        instancia.AtualizarStatus(status, telefone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Instância {InstanciaId} agora {Status}.", instancia.Id, status);
        return ResultadoWebhookAgente.Processado;
    }

    // ---------------------------------------------------------------- apoio

    private Task<InstanciaWhatsApp?> BuscarInstanciaAsync(string nomeInstancia, bool rastrear, CancellationToken cancellationToken)
    {
        // Único ponto sem negócio resolvido: o webhook só traz o nome da instância.
        var consulta = _dbContext.InstanciasWhatsApp.IgnoreQueryFilters().Where(i => i.NomeInstancia == nomeInstancia);
        return (rastrear ? consulta : consulta.AsNoTracking()).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Comparação em tempo constante; o hash SHA-256 iguala o tamanho (não vaza o comprimento do segredo).</summary>
    public static bool SegredoConfere(string? recebido, string esperado)
    {
        if (string.IsNullOrEmpty(recebido))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(recebido)), SHA256.HashData(Encoding.UTF8.GetBytes(esperado)));
    }

    private static string? Texto(JsonElement objeto, string propriedade) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(propriedade, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;
}
