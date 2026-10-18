namespace Plataforma.Aplicacao.WhatsAppAgente;

/// <summary>Instância de WhatsApp do negócio do usuário logado (painel). Sempre no negócio do token.</summary>
public interface IGerenciadorInstanciaWhatsApp
{
    /// <summary>Cria a instância se não existe e devolve o QR code para conectar. Evolution fora do ar → <see cref="FalhaEvolutionException"/>.</summary>
    Task<ConexaoWhatsApp> ConectarAsync(CancellationToken cancellationToken = default);

    /// <summary>Nulo se o negócio ainda não conectou. Consulta o estado atual na Evolution (se não responder, devolve o último gravado).</summary>
    Task<StatusWhatsApp?> ObterStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>False se o negócio ainda não tem instância.</summary>
    Task<bool> DefinirAgenteAsync(bool ativo, CancellationToken cancellationToken = default);
}

/// <param name="Status">"Desconectada", "AguardandoQrCode" ou "Conectada".</param>
public sealed record ConexaoWhatsApp(string Status, string? QrCode);

public sealed record StatusWhatsApp(string Status, bool AgenteAtivo, string? Telefone);
