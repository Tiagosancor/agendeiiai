using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Agendamentos;

/// <summary>Dados de um serviço no momento em que o agendamento é criado (seção 8.2.5: vários serviços ocupam um único intervalo contínuo).</summary>
public sealed record ItemServicoAgendamento(Guid ServicoId, string Nome, decimal Preco, int DuracaoMinutos);

/// <summary>
/// O coração do produto (seção 11, Sprint 2). A garantia real contra sobreposição é a
/// *exclusion constraint* do Postgres (seção 8.2.1) — esta classe só cuida das regras que
/// o banco não cobre sozinho: expediente, almoço, bloqueios, status e o ciclo de vida da
/// reserva temporária.
/// </summary>
public class Agendamento : EntidadeBase, IEntidadeDoNegocio
{
    private readonly List<AgendamentoServico> _servicos = [];

    public Guid NegocioId { get; private set; }

    public Guid ProfissionalId { get; private set; }

    /// <summary>
    /// Nulo só enquanto uma reserva do assistente público ainda não passou pela
    /// identificação do cliente (seção 8.1.4 — Sprint 3): o horário é reservado ao
    /// escolher o slot, antes de telefone/código existirem; <see cref="VincularCliente"/>
    /// preenche isso na confirmação. Todo outro caminho (painel) já cria com cliente
    /// definido.
    /// </summary>
    public Guid? ClienteId { get; private set; }

    public DateTimeOffset Inicio { get; private set; }

    public DateTimeOffset Fim { get; private set; }

    public StatusAgendamento Status { get; private set; }

    public string? Observacoes { get; private set; }

    /// <summary>Nome informado pelo cliente no link público, se diferente do cadastro (seção 8.1.4 — Sprint 3).</summary>
    public string? NomeInformado { get; private set; }

    /// <summary>Só tem valor enquanto <see cref="Status"/> é <see cref="StatusAgendamento.Reservado"/>.</summary>
    public DateTimeOffset? ReservadoAte { get; private set; }

    /// <summary>Cupom aplicado no resumo do assistente público (seção 6.2.4), revalidado no servidor na confirmação.</summary>
    public Guid? CupomId { get; private set; }

    /// <summary>Valor (em R$) descontado pelo cupom — o total exibido é sempre a soma dos serviços menos este valor.</summary>
    public decimal DescontoAplicado { get; private set; }

    /// <summary>
    /// Lembrete ao cliente antes do horário (seção 9, um só, antecedência configurável — padrão
    /// 60 min). Controla envio único mesmo se o job rodar mais de uma vez; remarcar zera.
    /// </summary>
    public bool LembreteEnviado { get; private set; }

    /// <summary>
    /// Quando o horário atual foi combinado com o cliente (confirmação ou remarcação). Nulo nos
    /// agendamentos antigos e nos do painel: vale o <c>CriadoEm</c>. Serve para não mandar
    /// "lembrete" minutos depois de a pessoa ter acabado de marcar em cima da hora.
    /// </summary>
    public DateTimeOffset? HorarioCombinadoEm { get; private set; }

    /// <summary>Consentimento do cliente no assistente público (seção 8.4) — data, IP e versão dos termos aceitos ao confirmar. Nulo em agendamentos criados pelo painel (não é o próprio cliente aceitando).</summary>
    public DateTimeOffset? ConsentimentoData { get; private set; }

    public string? ConsentimentoIp { get; private set; }

    public string? ConsentimentoVersaoTermos { get; private set; }

    public IReadOnlyCollection<AgendamentoServico> Servicos => _servicos.AsReadOnly();

    protected Agendamento()
    {
    }

    private Agendamento(
        Guid negocioId, Guid profissionalId, Guid? clienteId, DateTimeOffset inicio, DateTimeOffset fim, StatusAgendamento status)
    {
        NegocioId = negocioId;
        ProfissionalId = profissionalId;
        ClienteId = clienteId;
        Inicio = inicio;
        Fim = fim;
        Status = status;
    }

    /// <summary>Cria já confirmado — o fluxo do painel (Sprint 2), sem passar pela reserva temporária (só o fluxo público, Sprint 3, precisa dela).</summary>
    public static Agendamento CriarConfirmado(
        Guid negocioId, Guid profissionalId, Guid clienteId, DateTimeOffset inicio,
        IReadOnlyCollection<ItemServicoAgendamento> servicos, string? observacoes = null)
    {
        var agendamento = new Agendamento(
            negocioId, profissionalId, clienteId, inicio, CalcularFim(inicio, servicos), StatusAgendamento.Agendado)
        {
            Observacoes = observacoes,
        };

        agendamento.DefinirServicos(servicos);
        return agendamento;
    }

    /// <summary>
    /// Reserva temporária de 10 minutos (seção 8.2.2). <paramref name="clienteId"/> é nulo
    /// no assistente público — o horário é reservado antes de o cliente informar telefone
    /// e validar o código; <see cref="VincularCliente"/> completa isso na confirmação.
    /// </summary>
    public static Agendamento CriarReserva(
        Guid negocioId, Guid profissionalId, Guid? clienteId, DateTimeOffset inicio,
        IReadOnlyCollection<ItemServicoAgendamento> servicos, DateTimeOffset agora, TimeSpan duracaoDaReserva)
    {
        var agendamento = new Agendamento(
            negocioId, profissionalId, clienteId, inicio, CalcularFim(inicio, servicos), StatusAgendamento.Reservado)
        {
            ReservadoAte = agora + duracaoDaReserva,
        };

        agendamento.DefinirServicos(servicos);
        return agendamento;
    }

    private static DateTimeOffset CalcularFim(DateTimeOffset inicio, IReadOnlyCollection<ItemServicoAgendamento> servicos)
    {
        if (servicos.Count == 0)
            throw new ArgumentException("O agendamento precisa de pelo menos um serviço.", nameof(servicos));

        var duracaoTotal = servicos.Sum(s => s.DuracaoMinutos);
        return inicio.AddMinutes(duracaoTotal);
    }

    private void DefinirServicos(IReadOnlyCollection<ItemServicoAgendamento> servicos)
    {
        foreach (var item in servicos)
            _servicos.Add(new AgendamentoServico(NegocioId, Id, item.ServicoId, item.Nome, item.Preco, item.DuracaoMinutos));
    }

    /// <summary>
    /// Vincula o cliente identificado/criado após a validação do código (seção 8.1.4) a
    /// uma reserva anônima do assistente público. Chamado uma única vez, antes de
    /// <see cref="ConfirmarReserva"/>.
    /// </summary>
    public void VincularCliente(Guid clienteId)
    {
        if (Status != StatusAgendamento.Reservado)
            throw new InvalidOperationException("Só uma reserva pendente pode receber um cliente.");

        if (ClienteId is not null)
            throw new InvalidOperationException("Essa reserva já tem um cliente vinculado.");

        ClienteId = clienteId;
    }

    /// <summary>Aplica (ou reaplica) o desconto de um cupom, revalidado no servidor (seção 6.2.4/7).</summary>
    public void AplicarCupom(Guid cupomId, decimal desconto)
    {
        if (Status != StatusAgendamento.Reservado)
            throw new InvalidOperationException("Só uma reserva pendente pode receber um cupom.");

        CupomId = cupomId;
        DescontoAplicado = desconto;
    }

    public void ConfirmarReserva()
    {
        if (Status != StatusAgendamento.Reservado)
            throw new InvalidOperationException("Só uma reserva pendente pode ser confirmada.");

        if (ClienteId is null)
            throw new InvalidOperationException("A reserva precisa de um cliente vinculado antes de ser confirmada.");

        Status = StatusAgendamento.Agendado;
        ReservadoAte = null;
        HorarioCombinadoEm = DateTimeOffset.UtcNow;
    }

    /// <summary>Chamado pelo job (ou pela checagem antes de inserir) quando a reserva passou do prazo sem ser confirmada.</summary>
    public void Expirar()
    {
        if (Status != StatusAgendamento.Reservado)
            return;

        Status = StatusAgendamento.Expirado;
        ReservadoAte = null;
    }

    /// <summary>Move para um novo horário — quem chama precisa revalidar expediente/bloqueios; a exclusion constraint garante a ausência de sobreposição no SaveChanges (seção 8.2.7).</summary>
    public void Mover(DateTimeOffset novoInicio, DateTimeOffset novoFim, DateTimeOffset? agora = null)
    {
        if (Status is not (StatusAgendamento.Agendado or StatusAgendamento.Reservado))
            throw new InvalidOperationException("Só um agendamento ativo pode ser movido.");

        if (novoFim <= novoInicio)
            throw new ArgumentException("O fim precisa ser depois do início.", nameof(novoFim));

        // Horário novo, lembrete novo: o do horário antigo (se já saiu) não vale para este.
        if (novoInicio != Inicio)
        {
            LembreteEnviado = false;
            HorarioCombinadoEm = agora ?? DateTimeOffset.UtcNow;
        }

        Inicio = novoInicio;
        Fim = novoFim;
    }

    /// <summary>
    /// Passa para outro profissional no mesmo horário (exclusão de profissional, seção 7) — quem
    /// chama revalida serviços/expediente/bloqueios; a exclusion constraint cobre a sobreposição.
    /// </summary>
    public void TransferirPara(Guid novoProfissionalId)
    {
        if (Status is not (StatusAgendamento.Agendado or StatusAgendamento.Reservado))
            throw new InvalidOperationException("Só um agendamento ativo pode ser transferido.");

        ProfissionalId = novoProfissionalId;
    }

    public void Cancelar()
    {
        if (Status is StatusAgendamento.Cancelado or StatusAgendamento.Concluido)
            throw new InvalidOperationException($"Um agendamento {Status} não pode ser cancelado.");

        Status = StatusAgendamento.Cancelado;
        ReservadoAte = null;
    }

    public void MarcarConcluido()
    {
        if (Status != StatusAgendamento.Agendado)
            throw new InvalidOperationException("Só um agendamento confirmado pode virar concluído.");

        Status = StatusAgendamento.Concluido;
    }

    public void MarcarFaltou()
    {
        if (Status != StatusAgendamento.Agendado)
            throw new InvalidOperationException("Só um agendamento confirmado pode virar falta.");

        Status = StatusAgendamento.Faltou;
    }

    public void DefinirObservacoes(string? observacoes) => Observacoes = observacoes;

    public void DefinirNomeInformado(string? nomeInformado) => NomeInformado = nomeInformado;

    /// <summary>A janela do lembrete já abriu e ele ainda não saiu — só de agendamento que ainda vai acontecer (seção 8.5.6: descarta os vencidos).</summary>
    public bool NaJanelaDoLembrete(DateTimeOffset agora, TimeSpan antecedencia) =>
        Status == StatusAgendamento.Agendado && !LembreteEnviado && Inicio > agora && Inicio <= agora + antecedencia;

    /// <summary>
    /// O lembrete só faz sentido se o horário foi combinado com folga: se ele fosse chegar menos de
    /// <paramref name="intervaloMinimo"/> depois da marcação (ex.: marcou às 14h para as 14h50), a
    /// própria confirmação já cumpre o papel — mandar "lembrete" logo depois só confunde.
    /// </summary>
    public bool LembreteFazSentido(TimeSpan antecedencia, TimeSpan intervaloMinimo) =>
        Inicio - antecedencia >= (HorarioCombinadoEm ?? CriadoEm) + intervaloMinimo;

    /// <summary>Enviado — ou dispensado (marcado em cima da hora, ou horário já passou): nunca mais é considerado.</summary>
    public void MarcarLembreteEnviado() => LembreteEnviado = true;

    public void RegistrarConsentimento(DateTimeOffset agora, string ip, string versaoTermos)
    {
        ConsentimentoData = agora;
        ConsentimentoIp = ip;
        ConsentimentoVersaoTermos = versaoTermos;
    }

    /// <summary>Total sempre recalculado a partir da soma dos serviços menos o desconto (seção 6.2.4) — nunca guardado por fora.</summary>
    public decimal Total => _servicos.Sum(s => s.Preco) - DescontoAplicado;
}
