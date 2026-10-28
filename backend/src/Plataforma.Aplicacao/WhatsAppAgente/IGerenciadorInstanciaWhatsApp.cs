namespace Plataforma.Aplicacao.WhatsAppAgente;

/// <summary>Instância de WhatsApp do negócio do usuário logado (painel). Sempre no negócio do token.</summary>
public interface IGerenciadorInstanciaWhatsApp
{
    /// <summary>Cria a instância se não existe e devolve o QR code para conectar. Evolution fora do ar → <see cref="FalhaEvolutionException"/>.</summary>
    Task<ConexaoWhatsApp> ConectarAsync(CancellationToken cancellationToken = default);

    /// <summary>Nulo se o negócio ainda não conectou. Consulta o estado atual na Evolution (se não responder, devolve o último gravado).</summary>
    Task<StatusWhatsApp?> ObterStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Liga/desliga o agente na tela. Ligar sem o serviço de IA contratado é recusado (<see cref="ResultadoDefinirAgente.NaoContratado"/>);
    /// desligar é sempre aceito.
    /// </summary>
    Task<ResultadoDefinirAgente> DefinirAgenteAsync(bool ativo, CancellationToken cancellationToken = default);

    /// <summary>Situação do serviço de IA do negócio do token. Responde mesmo sem instância conectada.</summary>
    Task<ServicoAgenteIaPainel> ObterServicoIaAsync(CancellationToken cancellationToken = default);
}

public enum ResultadoDefinirAgente
{
    Ok = 0,

    /// <summary>O negócio ainda não tem instância.</summary>
    SemInstancia = 1,

    /// <summary>Ligar o agente sem o serviço de IA contratado.</summary>
    NaoContratado = 2,
}

/// <param name="Origem">"Manual", "Plano" ou "Adicional"; nula quando não contratado.</param>
public sealed record ServicoAgenteIaPainel(bool Contratado, string? Origem);

/// <param name="Status">"Desconectada", "AguardandoQrCode" ou "Conectada".</param>
public sealed record ConexaoWhatsApp(string Status, string? QrCode);

/// <param name="AgenteIaContratado">Serviço de IA contratado para o negócio; sem ele o agente não responde e não pode ser ligado.</param>
public sealed record StatusWhatsApp(string Status, bool AgenteAtivo, string? Telefone, bool AgenteIaContratado = false);
