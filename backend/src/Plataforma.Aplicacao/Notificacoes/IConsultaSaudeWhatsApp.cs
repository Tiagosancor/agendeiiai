namespace Plataforma.Aplicacao.Notificacoes;

/// <summary>
/// Estado da conexão do WhatsApp para a administração da plataforma (seção 7) — sem isso, uma
/// queda de sessão da instância só seria percebida quando um cliente reclamasse do código.
/// </summary>
public interface IConsultaSaudeWhatsApp
{
    Task<SaudeWhatsApp> ObterAsync(CancellationToken cancellationToken = default);
}

public enum EstadoConexaoWhatsApp
{
    /// <summary>O provedor configurado não tem sessão a acompanhar (Fake ou API oficial).</summary>
    NaoSeAplica,
    Conectada,
    Desconectada,
    /// <summary>A sessão caiu e a instância espera a leitura de um QR code novo.</summary>
    AguardandoQrCode,
    /// <summary>A instância não respondeu (fora do ar, URL ou chave erradas).</summary>
    Indisponivel,
}

public sealed record SaudeWhatsApp(string Provedor, EstadoConexaoWhatsApp Estado, string? Detalhe, DateTimeOffset ConsultadoEm);
