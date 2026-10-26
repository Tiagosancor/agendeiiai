using System.Collections.Concurrent;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Disjuntor em memória do modelo principal, com estado POR NEGÓCIO: depois de uma falha de capacidade (503/429/404/timeout...),
/// as próximas rodadas daquele negócio vão direto ao modelo de reserva por <see cref="Duracao"/>, em vez de esperar ~10 s por
/// mensagem para falhar de novo. Um negócio nunca afeta o outro. Singleton; por instância da API (não é compartilhado entre
/// réplicas, e reiniciar zera — ambos inofensivos). Entradas expiradas são removidas na própria consulta/abertura.
/// </summary>
public sealed class DisjuntorModeloIa
{
    public static readonly TimeSpan Duracao = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<Guid, long> _abertoAteTicks = new();

    public bool Aberto(Guid negocioId, DateTimeOffset agora)
    {
        if (!_abertoAteTicks.TryGetValue(negocioId, out var ate))
            return false;

        if (agora.UtcTicks < ate)
            return true;

        // Expirou: remove só se ainda for esta mesma entrada (outra thread pode ter reaberto).
        _abertoAteTicks.TryRemove(new KeyValuePair<Guid, long>(negocioId, ate));
        return false;
    }

    public void Abrir(Guid negocioId, DateTimeOffset agora)
    {
        var ate = (agora + Duracao).UtcTicks;
        _abertoAteTicks.AddOrUpdate(negocioId, ate, (_, atual) => Math.Max(atual, ate));

        // Varredura barata das expiradas de outros negócios, para o dicionário não crescer sem limite.
        foreach (var entrada in _abertoAteTicks)
        {
            if (entrada.Value <= agora.UtcTicks)
                _abertoAteTicks.TryRemove(entrada);
        }
    }
}
