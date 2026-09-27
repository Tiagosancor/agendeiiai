using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Notificacoes;

/// <summary>
/// Aviso ao profissional de um agendamento novo, remarcado ou cancelado (seção 9), com o
/// resultado de cada canal — é onde o webhook do WhatsApp grava se a mensagem saiu (seção 10).
/// </summary>
public class NotificacaoProfissional : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid AgendamentoId { get; private set; }

    public Guid ProfissionalId { get; private set; }

    /// <summary>"Novo", "Remarcado" ou "Cancelado".</summary>
    public string Evento { get; private set; } = string.Empty;

    public StatusCanal? CanalEmailStatus { get; private set; }

    public StatusCanal? CanalWhatsAppStatus { get; private set; }

    /// <summary>ID da mensagem no provedor de WhatsApp — a chave com que o webhook acha este registro.</summary>
    public string? IdMensagemWhatsApp { get; private set; }

    protected NotificacaoProfissional()
    {
    }

    public NotificacaoProfissional(
        Guid negocioId, Guid agendamentoId, Guid profissionalId, string evento,
        StatusCanal? canalEmailStatus, StatusCanal? canalWhatsAppStatus, string? idMensagemWhatsApp)
    {
        NegocioId = negocioId;
        AgendamentoId = agendamentoId;
        ProfissionalId = profissionalId;
        Evento = evento;
        CanalEmailStatus = canalEmailStatus;
        CanalWhatsAppStatus = canalWhatsAppStatus;
        IdMensagemWhatsApp = idMensagemWhatsApp;
    }

    public bool AtualizarStatusWhatsApp(StatusCanal novo)
    {
        if (!TransicaoStatusCanal.PodeMudar(CanalWhatsAppStatus, novo))
            return false;

        CanalWhatsAppStatus = novo;
        return true;
    }
}
