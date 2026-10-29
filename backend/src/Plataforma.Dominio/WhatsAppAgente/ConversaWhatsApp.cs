using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.WhatsAppAgente;

public enum StatusConversaWhatsApp
{
    Agente = 1,
    Humano = 2,
}

/// <summary>Como a conversa foi para o atendimento humano (Fase 4).</summary>
public enum MotivoHumanoConversa
{
    /// <summary>Alguém com permissão assumiu pelo painel.</summary>
    Painel = 1,

    /// <summary>O agente chamou um atendente (ferramenta <c>chamar_atendente</c>).</summary>
    PedidoDoAgente = 2,
}

/// <summary>Conversa de um cliente (por telefone) com uma instância. Única por (instância, telefone).</summary>
public class ConversaWhatsApp : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid InstanciaId { get; private set; }

    /// <summary>Telefone do cliente em E.164 (ex.: +5571988887777).</summary>
    public string TelefoneCliente { get; private set; } = string.Empty;

    public Guid? ClienteId { get; private set; }

    public StatusConversaWhatsApp Status { get; private set; } = StatusConversaWhatsApp.Agente;

    public DateTimeOffset UltimaMensagemEm { get; private set; }

    public string? Resumo { get; private set; }

    /// <summary>Trava (lease) da rodada do agente nesta conversa. Tomada por <c>UPDATE</c> condicional, não por esta propriedade.</summary>
    public DateTimeOffset? ProcessandoAte { get; private set; }

    /// <summary>Instante da última mensagem de usuário já tratada pelo agente (respondida ou descartada por limite).</summary>
    public DateTimeOffset? ProcessadaAte { get; private set; }

    /// <summary>Quando o aviso de atendimento automatizado foi enviado pela última vez (Fase 3; controla o reenvio a cada N dias).</summary>
    public DateTimeOffset? AvisoAutomatizadoEm { get; private set; }

    /// <summary>Desde quando a conversa está com atendimento humano. Nulo fora do modo Humano.</summary>
    public DateTimeOffset? HumanoDesde { get; private set; }

    /// <summary>
    /// Quando a devolução automática vale (última mensagem do cliente + prazo; renovado a cada mensagem dele). Nulo = sem prazo.
    /// A devolução é preguiçosa: acontece quando chega a próxima mensagem, não por job.
    /// </summary>
    public DateTimeOffset? HumanoAte { get; private set; }

    public MotivoHumanoConversa? HumanoMotivo { get; private set; }

    /// <summary>Quem assumiu pelo painel. Atalho sem FK (o usuário pode ser excluído).</summary>
    public Guid? HumanoPorUsuarioId { get; private set; }

    /// <summary>
    /// Quando o atendimento voltou ao agente. As mensagens anteriores não contam nos limites por telefone nem entram no histórico
    /// do modelo (o humano tratou o assunto sem o agente ver as respostas dele).
    /// </summary>
    public DateTimeOffset? DevolvidaEm { get; private set; }

    /// <summary>Último e-mail de aviso ao atendente por <c>chamar_atendente</c> (limita a 1 a cada N horas por conversa).</summary>
    public DateTimeOffset? AtendenteAvisadoEm { get; private set; }

    /// <summary>
    /// Época do atendimento: sobe a cada assumir/devolver (pelo painel ou pelo prazo). A rodada do agente guarda a época em que começou;
    /// se ela mudou, a rodada é de antes da transição e não responde nem chama ferramenta (exceto fato já efetivado). Não é um token de
    /// concorrência do EF: as transições são <c>UPDATE</c> condicionais.
    /// </summary>
    public int EpocaAtendimento { get; private set; }

    protected ConversaWhatsApp()
    {
    }

    public ConversaWhatsApp(Guid negocioId, Guid instanciaId, string telefoneCliente, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(telefoneCliente))
            throw new ArgumentException("O telefone do cliente é obrigatório.", nameof(telefoneCliente));

        NegocioId = negocioId;
        InstanciaId = instanciaId;
        TelefoneCliente = telefoneCliente;
        UltimaMensagemEm = agora;
    }

    public void RegistrarMensagem(DateTimeOffset em)
    {
        if (em > UltimaMensagemEm)
            UltimaMensagemEm = em;
    }

    /// <summary>Atalho para o cliente identificado na confirmação. A busca do cliente é sempre pelo telefone, nunca por este campo.</summary>
    public void VincularCliente(Guid clienteId) => ClienteId = clienteId;

    /// <summary>Usado na anonimização do cliente (LGPD): a conversa deixa de apontar para ele.</summary>
    public void DesvincularCliente() => ClienteId = null;

    /// <summary>Prefixo do telefone de uma conversa anonimizada (LGPD). O restante é um pedaço do <c>Id</c>: a coluna tem 20 posições.</summary>
    public const string PrefixoTelefoneAnonimizado = "anonimizado:";

    public bool Anonimizada => TelefoneCliente.StartsWith(PrefixoTelefoneAnonimizado, StringComparison.Ordinal);

    /// <summary>
    /// LGPD (G11): tira da linha tudo o que identifica a pessoa — telefone (vira <c>anonimizado:xxxxxxxx</c>, que cabe nas 20 posições da
    /// coluna e mantém o índice único por instância+telefone), cliente e resumo. A linha FICA porque <c>usos_ia</c> e <c>rodadas_agente</c>
    /// dependem dela (cota e diagnóstico, só números). Volta a <see cref="StatusConversaWhatsApp.Agente"/> e perde o lease: uma rodada em
    /// andamento deixa de renovar, marcar e (conferindo antes) iniciar NOVOS envios, e toda gravação de mensagem na conversa é recusada
    /// (<c>PlataformaDbContext</c>, sob FOR NO KEY UPDATE). LIMITE: um envio que já passou da última conferência e está no provedor não é cancelado — o
    /// texto já entregue não é recuperável; o que se garante é que nada novo é gravado nem marcado depois. O mesmo número escrevendo depois
    /// abre uma conversa nova.
    /// </summary>
    /// <param name="telefonesEmUso">Telefones já usados por outras conversas da mesma instância (evita colisão no índice único).</param>
    public void Anonimizar(ISet<string> telefonesEmUso)
    {
        var texto = Id.ToString("N");
        string? escolhido = null;
        for (var inicio = 0; inicio + 8 <= texto.Length && escolhido is null; inicio += 8)
        {
            var candidato = PrefixoTelefoneAnonimizado + texto.Substring(inicio, 8);
            if (!telefonesEmUso.Contains(candidato))
                escolhido = candidato;
        }

        // Quatro pedaços de 32 bits de um Guid aleatório todos ocupados: praticamente impossível; cai no último.
        TelefoneCliente = escolhido ?? PrefixoTelefoneAnonimizado + texto[^8..];
        telefonesEmUso.Add(TelefoneCliente);
        ClienteId = null;
        Resumo = null;
        Status = StatusConversaWhatsApp.Agente;
        ProcessandoAte = null;
        LimparEstadoHumano();
        DevolvidaEm = null;
        AtendenteAvisadoEm = null;
    }

    // ---------------------------------------------------------------- atendimento humano (Fase 4)

    /// <summary>
    /// Assume pelo painel. As transições reais no banco são atômicas (<c>UPDATE</c> condicional no gerenciador de conversas); estes
    /// métodos são a mesma regra no domínio. Idempotente: já em Humano, só registra quem atende (se ainda ninguém).
    /// </summary>
    public bool AssumirPeloPainel(Guid usuarioId, DateTimeOffset agora, TimeSpan? devolverApos)
    {
        if (Status == StatusConversaWhatsApp.Humano)
        {
            if (HumanoPorUsuarioId is not null)
                return false;

            HumanoPorUsuarioId = usuarioId;
            return true;
        }

        EntrarEmHumano(MotivoHumanoConversa.Painel, agora, devolverApos);
        EpocaAtendimento++;
        HumanoPorUsuarioId = usuarioId;
        return true;
    }

    /// <summary>O agente chamou um atendente. Só sai de <see cref="StatusConversaWhatsApp.Agente"/>; já em Humano não muda nada.</summary>
    public bool ChamarAtendentePeloAgente(DateTimeOffset agora, TimeSpan? devolverApos)
    {
        if (Status == StatusConversaWhatsApp.Humano || Anonimizada)
            return false;

        EntrarEmHumano(MotivoHumanoConversa.PedidoDoAgente, agora, devolverApos);
        return true;
    }

    /// <summary>
    /// Devolve ao agente. <paramref name="ultimaMensagemDoCliente"/> vira o "processado até": o acumulado durante o atendimento humano
    /// nunca é respondido — o agente só responde a próxima mensagem nova.
    /// </summary>
    public bool Devolver(DateTimeOffset agora, DateTimeOffset? ultimaMensagemDoCliente)
    {
        if (Status != StatusConversaWhatsApp.Humano)
            return false;

        Status = StatusConversaWhatsApp.Agente;
        LimparEstadoHumano();
        EpocaAtendimento++;
        DevolvidaEm = agora;
        if (ultimaMensagemDoCliente is { } ultima && (ProcessadaAte is null || ProcessadaAte < ultima))
            ProcessadaAte = ultima;
        return true;
    }

    /// <summary>Cada mensagem do cliente em atendimento humano adia a devolução automática. Sem prazo (nulo em <c>HumanoAte</c>) não muda.</summary>
    public void RenovarPrazoHumano(DateTimeOffset agora, TimeSpan? devolverApos)
    {
        if (Status == StatusConversaWhatsApp.Humano && HumanoAte is not null && devolverApos is { } prazo)
            HumanoAte = agora + prazo;
    }

    /// <summary>O prazo da devolução automática já passou? (A devolução em si é feita quando chega a próxima mensagem.)</summary>
    public bool PrazoHumanoVencido(DateTimeOffset agora) =>
        Status == StatusConversaWhatsApp.Humano && HumanoAte is { } ate && ate <= agora;

    public void RegistrarAtendenteAvisado(DateTimeOffset em) => AtendenteAvisadoEm = em;

    private void EntrarEmHumano(MotivoHumanoConversa motivo, DateTimeOffset agora, TimeSpan? devolverApos)
    {
        Status = StatusConversaWhatsApp.Humano;
        HumanoDesde = agora;
        HumanoMotivo = motivo;
        HumanoPorUsuarioId = null;
        HumanoAte = devolverApos is { } prazo ? agora + prazo : null;
    }

    private void LimparEstadoHumano()
    {
        HumanoDesde = null;
        HumanoAte = null;
        HumanoMotivo = null;
        HumanoPorUsuarioId = null;
    }

    public void RegistrarAvisoAutomatizado(DateTimeOffset em) => AvisoAutomatizadoEm = em;

    public bool AgenteResponde => Status == StatusConversaWhatsApp.Agente;
}

/// <summary>
/// Um aviso por e-mail ao atendente autorizado (<c>chamar_atendente</c>). Existe só para o teto diário por negócio contar AVISOS, e não
/// conversas: gravado na mesma transação da decisão, sob a trava por negócio/dia. Sem texto nem telefone.
/// </summary>
public class AvisoAtendenteWhatsApp : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid ConversaId { get; private set; }

    public DateTimeOffset Em { get; private set; }

    protected AvisoAtendenteWhatsApp()
    {
    }

    public AvisoAtendenteWhatsApp(Guid negocioId, Guid conversaId, DateTimeOffset em)
    {
        NegocioId = negocioId;
        ConversaId = conversaId;
        Em = em;
    }
}
