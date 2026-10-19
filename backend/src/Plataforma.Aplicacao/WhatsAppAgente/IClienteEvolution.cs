namespace Plataforma.Aplicacao.WhatsAppAgente;

/// <summary>Cliente HTTP da Evolution API para o agente (uma instância por negócio). Falha do provedor lança <see cref="FalhaEvolutionException"/>.</summary>
public interface IClienteEvolution
{
    /// <summary>Cria a instância já com o webhook por instância apontando para <paramref name="urlWebhook"/>. Devolve o QR code (data URI), se vier.</summary>
    Task<string?> CriarInstanciaAsync(string nomeInstancia, string? urlWebhook, CancellationToken cancellationToken = default);

    /// <summary>Inicia a conexão e devolve o QR code (data URI); nulo se a instância já está conectada.</summary>
    Task<string?> ObterQrCodeAsync(string nomeInstancia, CancellationToken cancellationToken = default);

    Task<EstadoInstanciaEvolution> ConsultarStatusAsync(string nomeInstancia, CancellationToken cancellationToken = default);

    /// <summary>Envia texto e devolve o ID da mensagem no provedor, se informado.</summary>
    Task<string?> EnviarTextoAsync(string nomeInstancia, string telefoneE164, string texto, CancellationToken cancellationToken = default);
}

public enum EstadoInstanciaEvolution
{
    NaoExiste = 0,
    Fechada = 1,
    Conectando = 2,
    Aberta = 3,
}

public sealed class FalhaEvolutionException : Exception
{
    public FalhaEvolutionException(string mensagem, Exception? interna = null) : base(mensagem, interna)
    {
    }
}
