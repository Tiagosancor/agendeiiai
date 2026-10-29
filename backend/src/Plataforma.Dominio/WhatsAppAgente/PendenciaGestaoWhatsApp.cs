using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.WhatsAppAgente;

public enum TipoGestaoWhatsApp
{
    /// <summary>Cancelar um agendamento do cliente (Fase 4, Fatia 7).</summary>
    Cancelar = 1,

    /// <summary>Remarcar um agendamento do cliente (Fatia 8): move o MESMO agendamento no lugar, sem segurar o novo horário.</summary>
    Remarcar = 2,
}

/// <summary>
/// Pedido de CANCELAR ou REMARCAR (Fatia 8) um agendamento do cliente pelo agente de WhatsApp (Fase 4). Só o servidor o efetiva,
/// depois do "SIM" do cliente ao resumo que o próprio servidor montou — o modelo só prepara. Não segura nenhum horário (diferente da
/// pendência de agendamento): quem expira só encerra o pedido. Tabela própria de propósito: a versão anterior do código não a enxerga,
/// então um rollback nunca trata um pedido de cancelamento como reserva a confirmar. No máximo uma <c>Aguardando</c> por conversa.
/// Os valores de <see cref="Status"/> são os mesmos da pendência de agendamento.
/// </summary>
public class PendenciaGestaoWhatsApp : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid ConversaId { get; private set; }

    public Guid AgendamentoId { get; private set; }

    public TipoGestaoWhatsApp Tipo { get; private set; }

    /// <summary>Novo horário (UTC) pedido; só na remarcação.</summary>
    public DateTimeOffset? NovoInicio { get; private set; }

    /// <summary>
    /// Retrato do que o cliente VIU no resumo da remarcação (Fatia 8): profissional, início atual e duração do agendamento. Se algum deles
    /// mudou até o "SIM" (transferência, remarcação pelo link ou pelo painel, serviço trocado) a remarcação é recusada. Só na remarcação.
    /// </summary>
    public Guid? ProfissionalId { get; private set; }

    public DateTimeOffset? InicioAnterior { get; private set; }

    public int? DuracaoMinutos { get; private set; }

    public StatusPendenciaAgendamentoWhatsApp Status { get; private set; } = StatusPendenciaAgendamentoWhatsApp.Aguardando;

    public DateTimeOffset ExpiraEm { get; private set; }

    public Guid? ResumoMensagemId { get; private set; }

    public DateTimeOffset? ResumoEnviadoEm { get; private set; }

    public Guid? ConfirmacaoMensagemId { get; private set; }

    public DateTimeOffset? ConcluidaEm { get; private set; }

    /// <summary>
    /// Identificador ALEATÓRIO da operação que efetivou (nunca um instante): só quem o conhece se reconhece como "quem efetivou agora",
    /// inclusive numa nova tentativa depois de COMMIT ambíguo.
    /// </summary>
    public Guid? OperacaoId { get; private set; }

    public bool Aguardando => Status == StatusPendenciaAgendamentoWhatsApp.Aguardando;

    protected PendenciaGestaoWhatsApp()
    {
    }

    public PendenciaGestaoWhatsApp(
        Guid negocioId, Guid conversaId, Guid agendamentoId, TipoGestaoWhatsApp tipo, DateTimeOffset agora, DateTimeOffset expiraEm,
        DateTimeOffset? novoInicio = null, Guid? profissionalId = null, DateTimeOffset? inicioAnterior = null, int? duracaoMinutos = null)
    {
        if (expiraEm <= agora)
            throw new ArgumentException("A pendência precisa expirar depois de criada.", nameof(expiraEm));
        if (tipo == TipoGestaoWhatsApp.Remarcar && novoInicio is null)
            throw new ArgumentException("A remarcação precisa do novo horário.", nameof(novoInicio));
        if (tipo == TipoGestaoWhatsApp.Remarcar && (profissionalId is null || inicioAnterior is null || duracaoMinutos is null or <= 0))
            throw new ArgumentException("A remarcação precisa do retrato do agendamento (profissional, início e duração).", nameof(profissionalId));

        NegocioId = negocioId;
        ConversaId = conversaId;
        AgendamentoId = agendamentoId;
        Tipo = tipo;
        NovoInicio = tipo == TipoGestaoWhatsApp.Remarcar ? novoInicio : null;
        ProfissionalId = tipo == TipoGestaoWhatsApp.Remarcar ? profissionalId : null;
        InicioAnterior = tipo == TipoGestaoWhatsApp.Remarcar ? inicioAnterior : null;
        DuracaoMinutos = tipo == TipoGestaoWhatsApp.Remarcar ? duracaoMinutos : null;
        CriadoEm = agora;
        ExpiraEm = expiraEm;
    }

    public bool Vencida(DateTimeOffset agora) => agora >= ExpiraEm;

    /// <summary>O resumo (texto do servidor) já foi gravado e enviado: marca a mensagem e o instante.</summary>
    public void RegistrarResumoEnviado(Guid mensagemId, DateTimeOffset em)
    {
        ExigirAguardando();
        ResumoMensagemId = mensagemId;
        ResumoEnviadoEm = em;
    }

    public void Efetivar(DateTimeOffset agora, Guid? confirmacaoMensagemId, Guid operacaoId)
    {
        ExigirAguardando();
        Concluir(StatusPendenciaAgendamentoWhatsApp.Confirmada, agora);
        ConfirmacaoMensagemId = confirmacaoMensagemId;
        OperacaoId = operacaoId;
    }

    /// <summary>Foi esta operação que efetivou a pendência?</summary>
    public bool EfetivadaPor(Guid operacaoId) => Status == StatusPendenciaAgendamentoWhatsApp.Confirmada && OperacaoId == operacaoId;

    public void Recusar(DateTimeOffset agora) => Encerrar(StatusPendenciaAgendamentoWhatsApp.Recusada, agora);

    public void Expirar(DateTimeOffset agora) => Encerrar(StatusPendenciaAgendamentoWhatsApp.Expirada, agora);

    public void Substituir(DateTimeOffset agora) => Encerrar(StatusPendenciaAgendamentoWhatsApp.Substituida, agora);

    public void Falhar(DateTimeOffset agora) => Encerrar(StatusPendenciaAgendamentoWhatsApp.Falhou, agora);

    private void Encerrar(StatusPendenciaAgendamentoWhatsApp status, DateTimeOffset agora)
    {
        ExigirAguardando();
        Concluir(status, agora);
    }

    private void Concluir(StatusPendenciaAgendamentoWhatsApp status, DateTimeOffset agora)
    {
        Status = status;
        ConcluidaEm = agora;
    }

    private void ExigirAguardando()
    {
        if (Status != StatusPendenciaAgendamentoWhatsApp.Aguardando)
            throw new InvalidOperationException($"A pendência já está {Status}.");
    }
}
