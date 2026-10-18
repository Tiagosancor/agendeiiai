namespace Plataforma.Aplicacao.WhatsAppAgente;

/// <summary>
/// Primeira barreira contra webhook repetido. Se o armazenamento (Redis) estiver fora do ar ou não
/// configurado, devolve <c>true</c> (segue em frente): o índice único do banco é a garantia real.
/// </summary>
public interface IDeduplicadorMensagens
{
    /// <summary>True se é a primeira vez que vemos a mensagem (ou se não foi possível conferir).</summary>
    Task<bool> RegistrarSeNovaAsync(string nomeInstancia, string idMensagem, CancellationToken cancellationToken = default);
}
