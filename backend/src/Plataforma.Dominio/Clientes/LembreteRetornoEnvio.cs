using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Clientes;

public enum StatusLembreteRetorno
{
    /// <summary>
    /// Reservado pelo job antes de chamar o provedor: o índice único (cliente, atendimento) garante que só uma execução
    /// envia. Se a execução morrer aqui, o registro fica assim e não é reenviado — melhor faltar um lembrete que duplicar.
    /// </summary>
    Enviando = 1,
    Enviado = 2,
    Falhou = 3,

    /// <summary>
    /// Pode ter saído, mas não há como saber (timeout do provedor, queda no meio do envio). Nunca é reenviado — melhor faltar um
    /// lembrete que duplicar — e não conta como enviado nos relatórios.
    /// </summary>
    Desconhecido = 4,
}

/// <summary>
/// Um lembrete de retorno por atendimento concluído de referência (índice único em <c>(ClienteId, AgendamentoId)</c>):
/// é a idempotência do job e a base do relatório. Uma falha ganha no máximo mais uma tentativa, no mesmo dia.
/// </summary>
public class LembreteRetornoEnvio : EntidadeBase, IEntidadeDoNegocio
{
    public const int MaximoTentativas = 2;

    public Guid NegocioId { get; private set; }

    public Guid ClienteId { get; private set; }

    /// <summary>O atendimento concluído de referência (o último do cliente quando o lembrete foi decidido).</summary>
    public Guid AgendamentoId { get; private set; }

    /// <summary>Dia (no fuso do negócio) em que o envio foi decidido — a nova tentativa só vale neste mesmo dia.</summary>
    public DateOnly DiaEnvio { get; private set; }

    public StatusLembreteRetorno Status { get; private set; }

    public int Tentativas { get; private set; }

    /// <summary>Última tentativa (com sucesso ou não).</summary>
    public DateTimeOffset EnviadoEm { get; private set; }

    /// <summary>Motivo técnico da falha, sem dado pessoal (nunca telefone nem texto da mensagem).</summary>
    public string? Erro { get; private set; }

    public string? IdMensagem { get; private set; }

    /// <summary>Primeiro agendamento que o cliente fez até 7 dias depois do envio (relatório). Preenchido pelo job.</summary>
    public Guid? AgendamentoGeradoId { get; private set; }

    protected LembreteRetornoEnvio()
    {
    }

    public static LembreteRetornoEnvio Iniciar(Guid negocioId, Guid clienteId, Guid agendamentoId, DateOnly diaEnvio, DateTimeOffset agora) =>
        new()
        {
            NegocioId = negocioId,
            ClienteId = clienteId,
            AgendamentoId = agendamentoId,
            DiaEnvio = diaEnvio,
            Status = StatusLembreteRetorno.Enviando,
            Tentativas = 1,
            EnviadoEm = agora,
        };

    public bool PodeTentarDeNovo(DateOnly hoje) =>
        Status == StatusLembreteRetorno.Falhou && Tentativas < MaximoTentativas && DiaEnvio == hoje;

    public void NovaTentativa(DateTimeOffset agora)
    {
        if (Status != StatusLembreteRetorno.Falhou || Tentativas >= MaximoTentativas)
            throw new InvalidOperationException("Este lembrete não pode ser tentado de novo.");

        Status = StatusLembreteRetorno.Enviando;
        Tentativas++;
        EnviadoEm = agora;
        Erro = null;
    }

    public void MarcarEnviado(string? idMensagem, DateTimeOffset agora)
    {
        Status = StatusLembreteRetorno.Enviado;
        IdMensagem = idMensagem;
        EnviadoEm = agora;
        Erro = null;
    }

    public void MarcarFalha(string erro, DateTimeOffset agora)
    {
        Status = StatusLembreteRetorno.Falhou;
        Erro = erro.Length > 500 ? erro[..500] : erro;
        EnviadoEm = agora;
    }

    /// <summary>Resultado ambíguo (outbox): fica registrado, sem nova tentativa e fora da contagem de enviados.</summary>
    public void MarcarDesconhecido(string erro, DateTimeOffset agora)
    {
        Status = StatusLembreteRetorno.Desconhecido;
        Erro = erro.Length > 500 ? erro[..500] : erro;
        EnviadoEm = agora;
    }

    public void MarcarAgendamentoGerado(Guid agendamentoId) => AgendamentoGeradoId ??= agendamentoId;
}
