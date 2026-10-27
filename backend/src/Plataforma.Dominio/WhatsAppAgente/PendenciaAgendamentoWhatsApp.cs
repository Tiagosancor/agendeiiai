using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.WhatsAppAgente;

public enum StatusPendenciaAgendamentoWhatsApp
{
    /// <summary>Reserva feita, resumo enviado (ou a enviar), esperando o "sim" do cliente.</summary>
    Aguardando = 1,
    Confirmada = 2,
    Recusada = 3,
    Expirada = 4,

    /// <summary>Trocada por um preparo novo da mesma conversa.</summary>
    Substituida = 5,

    /// <summary>Não pôde ser confirmada (profissional saiu, bloqueio novo, resumo não entregue...).</summary>
    Falhou = 6,
}

/// <summary>
/// Pedido de agendamento pelo agente de WhatsApp (Fase 3) que já tem o horário reservado (<c>Reservado</c>, 10 min) e
/// espera a resposta afirmativa do cliente. Só o servidor a confirma, nunca o modelo. No máximo uma <c>Aguardando</c> por
/// conversa (índice único parcial). Guarda a evidência do consentimento (ids das mensagens do resumo e do "sim").
/// </summary>
public class PendenciaAgendamentoWhatsApp : EntidadeBase, IEntidadeDoNegocio
{
    public const int TamanhoMaximoNome = 80;

    public Guid NegocioId { get; private set; }

    public Guid ConversaId { get; private set; }

    public Guid AgendamentoId { get; private set; }

    public StatusPendenciaAgendamentoWhatsApp Status { get; private set; } = StatusPendenciaAgendamentoWhatsApp.Aguardando;

    public DateTimeOffset ExpiraEm { get; private set; }

    public Guid? ResumoMensagemId { get; private set; }

    public DateTimeOffset? ResumoEnviadoEm { get; private set; }

    public Guid? ConfirmacaoMensagemId { get; private set; }

    public DateTimeOffset? ConcluidaEm { get; private set; }

    /// <summary>
    /// Identificador ALEATÓRIO da operação que confirmou (gerado antes da estratégia de retentativa e igual em todas as
    /// tentativas dela). Só quem o conhece se reconhece como "quem confirmou agora"; instantes não servem (podem colidir).
    /// </summary>
    public Guid? ConfirmacaoOperacaoId { get; private set; }

    /// <summary>Nome que o cliente novo digitou. Apagado assim que a pendência sai de <c>Aguardando</c>.</summary>
    public string? NomeInformado { get; private set; }

    public string? VersaoTermos { get; private set; }

    public bool Aguardando => Status == StatusPendenciaAgendamentoWhatsApp.Aguardando;

    protected PendenciaAgendamentoWhatsApp()
    {
    }

    public PendenciaAgendamentoWhatsApp(
        Guid negocioId, Guid conversaId, Guid agendamentoId, DateTimeOffset agora, DateTimeOffset expiraEm, string? nomeInformado = null)
    {
        if (expiraEm <= agora)
            throw new ArgumentException("A pendência precisa expirar depois de criada.", nameof(expiraEm));

        var nome = string.IsNullOrWhiteSpace(nomeInformado) ? null : nomeInformado.Trim();
        if (nome is { Length: > TamanhoMaximoNome })
            throw new ArgumentException($"O nome tem no máximo {TamanhoMaximoNome} caracteres.", nameof(nomeInformado));

        NegocioId = negocioId;
        ConversaId = conversaId;
        AgendamentoId = agendamentoId;
        CriadoEm = agora;
        ExpiraEm = expiraEm;
        NomeInformado = nome;
    }

    public bool Vencida(DateTimeOffset agora) => agora >= ExpiraEm;

    /// <summary>O resumo (texto do servidor) já foi gravado e enviado: marca a mensagem e o instante.</summary>
    public void RegistrarResumoEnviado(Guid mensagemId, DateTimeOffset em)
    {
        ExigirAguardando();
        ResumoMensagemId = mensagemId;
        ResumoEnviadoEm = em;
    }

    public void Confirmar(DateTimeOffset agora, Guid? confirmacaoMensagemId, string versaoTermos, Guid operacaoId)
    {
        ExigirAguardando();
        Concluir(StatusPendenciaAgendamentoWhatsApp.Confirmada, agora);
        ConfirmacaoMensagemId = confirmacaoMensagemId;
        VersaoTermos = versaoTermos;
        ConfirmacaoOperacaoId = operacaoId;
    }

    /// <summary>Foi esta operação que confirmou a pendência?</summary>
    public bool ConfirmadaPor(Guid operacaoId) => Status == StatusPendenciaAgendamentoWhatsApp.Confirmada && ConfirmacaoOperacaoId == operacaoId;

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
        NomeInformado = null;
    }

    private void ExigirAguardando()
    {
        if (Status != StatusPendenciaAgendamentoWhatsApp.Aguardando)
            throw new InvalidOperationException($"A pendência já está {Status}.");
    }
}
