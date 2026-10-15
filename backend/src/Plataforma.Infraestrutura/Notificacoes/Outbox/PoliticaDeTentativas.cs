using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Notificacoes.Outbox;

/// <summary>Para onde o item vai depois de uma tentativa (ou de um cancelamento): o que o despachante grava, fenceado pela reivindicação.</summary>
/// <param name="ProximaTentativaEm">Só em <see cref="StatusNotificacao.Pendente"/>.</param>
/// <param name="Tentativas">Total de chamadas ao provedor iniciadas, já contando a que acabou de acontecer.</param>
public sealed record Transicao(
    StatusNotificacao Status, int Tentativas, DateTimeOffset Agora, string? Codigo = null,
    DateTimeOffset? ProximaTentativaEm = null, string? IdMensagem = null);

/// <summary>
/// A política de tentativas, explícita e testável: dado o resultado, decide entre enviada, nova tentativa (com a espera da família),
/// falha definitiva, "desconhecida" e cancelada. Esperas e prazos vêm de <see cref="OpcoesNotificacoes"/>.
/// <list type="bullet">
/// <item>Lembrete de 60 min — imediato + até 3 novas tentativas (1, 5 e 15 min); vence 30 min antes do atendimento (cancelada, sem envio).</item>
/// <item>Operacionais — no máximo 5 tentativas (imediata, +1 min, +5 min, +1 h, +6 h) dentro de 24 h; passou disso, falha definitiva.</item>
/// <item>Retorno — como o lembrete, vence na validade configurada.</item>
/// </list>
/// </summary>
public sealed class PoliticaDeTentativas
{
    private readonly OpcoesNotificacoes _opcoes;

    public PoliticaDeTentativas(OpcoesNotificacoes opcoes) => _opcoes = opcoes;

    public Transicao AposResultado(NotificacaoPendente item, int tentativasFeitas, ResultadoDespacho resultado, DateTimeOffset agora) =>
        resultado.Classe switch
        {
            ClasseDespacho.Aceito => new Transicao(StatusNotificacao.Enviada, tentativasFeitas, agora, IdMensagem: resultado.IdMensagem),
            ClasseDespacho.FalhaDefinitiva => new Transicao(StatusNotificacao.Falhou, tentativasFeitas, agora, CodigosOutbox.RecusadaProvedor),
            ClasseDespacho.Ambiguo => new Transicao(StatusNotificacao.Desconhecida, tentativasFeitas, agora, CodigosOutbox.TimeoutAmbiguo),
            _ => AposFalhaRetentavel(item, tentativasFeitas, agora, CodigosOutbox.FalhaProvedor),
        };

    /// <summary>Falha confirmada sem aceite (ou erro nosso antes de chamar o provedor): outra tentativa, se couber no número e no prazo.</summary>
    public Transicao AposFalhaRetentavel(NotificacaoPendente item, int tentativasFeitas, DateTimeOffset agora, string codigo)
    {
        var familia = item.Tipo.Familia();
        var esperas = _opcoes.Esperas(familia);

        if (tentativasFeitas >= esperas.Count + 1)
            return new Transicao(StatusNotificacao.Falhou, tentativasFeitas, agora, CodigosOutbox.TentativasEsgotadas);

        var proxima = agora + esperas[tentativasFeitas - 1];
        if (proxima >= item.ExpiraEm)
            return Vencida(item, tentativasFeitas, agora);

        return new Transicao(StatusNotificacao.Pendente, tentativasFeitas, agora, codigo, proxima);
    }

    /// <summary>O prazo acabou: lembretes (que perdem o sentido) são cancelados; as operacionais viram falha definitiva.</summary>
    public static Transicao Vencida(NotificacaoPendente item, int tentativasFeitas, DateTimeOffset agora) =>
        item.Tipo.Familia() == FamiliaNotificacao.Operacional
            ? new Transicao(StatusNotificacao.Falhou, tentativasFeitas, agora, CodigosOutbox.PrazoEsgotado)
            : new Transicao(StatusNotificacao.Cancelada, tentativasFeitas, agora, CodigosOutbox.Expirada);

    public static Transicao Cancelada(NotificacaoPendente item, DateTimeOffset agora, string codigo) =>
        new(StatusNotificacao.Cancelada, item.Tentativas, agora, codigo);
}
