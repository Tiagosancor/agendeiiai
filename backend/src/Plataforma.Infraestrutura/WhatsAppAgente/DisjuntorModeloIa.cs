namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Disjuntor em memória do modelo principal: depois de uma falha de capacidade (503/429/404/timeout...), as próximas rodadas
/// vão direto ao modelo de reserva por <see cref="Duracao"/>, em vez de esperar ~10 s por mensagem para falhar de novo. Singleton;
/// por instância da API (não é compartilhado entre réplicas, e reiniciar zera — ambos inofensivos).
/// </summary>
public sealed class DisjuntorModeloIa
{
    public static readonly TimeSpan Duracao = TimeSpan.FromSeconds(60);

    private long _abertoAteTicks;

    public bool Aberto(DateTimeOffset agora) => agora.UtcTicks < Interlocked.Read(ref _abertoAteTicks);

    public void Abrir(DateTimeOffset agora) => Interlocked.Exchange(ref _abertoAteTicks, (agora + Duracao).UtcTicks);
}
