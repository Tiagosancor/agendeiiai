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

    public Guid ClienteId { get; private set; }

    public DateTimeOffset Inicio { get; private set; }

    public DateTimeOffset Fim { get; private set; }

    public StatusAgendamento Status { get; private set; }

    public string? Observacoes { get; private set; }

    /// <summary>Nome informado pelo cliente no link público, se diferente do cadastro (seção 8.1.4 — Sprint 3).</summary>
    public string? NomeInformado { get; private set; }

    /// <summary>Só tem valor enquanto <see cref="Status"/> é <see cref="StatusAgendamento.Reservado"/>.</summary>
    public DateTimeOffset? ReservadoAte { get; private set; }

    public IReadOnlyCollection<AgendamentoServico> Servicos => _servicos.AsReadOnly();

    protected Agendamento()
    {
    }

    private Agendamento(
        Guid negocioId, Guid profissionalId, Guid clienteId, DateTimeOffset inicio, DateTimeOffset fim, StatusAgendamento status)
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

    /// <summary>Reserva temporária de 10 minutos (seção 8.2.2) — usada pelo assistente público (Sprint 3); testada aqui como mecanismo de infraestrutura.</summary>
    public static Agendamento CriarReserva(
        Guid negocioId, Guid profissionalId, Guid clienteId, DateTimeOffset inicio,
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

    public void ConfirmarReserva()
    {
        if (Status != StatusAgendamento.Reservado)
            throw new InvalidOperationException("Só uma reserva pendente pode ser confirmada.");

        Status = StatusAgendamento.Agendado;
        ReservadoAte = null;
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
    public void Mover(DateTimeOffset novoInicio, DateTimeOffset novoFim)
    {
        if (Status is not (StatusAgendamento.Agendado or StatusAgendamento.Reservado))
            throw new InvalidOperationException("Só um agendamento ativo pode ser movido.");

        if (novoFim <= novoInicio)
            throw new ArgumentException("O fim precisa ser depois do início.", nameof(novoFim));

        Inicio = novoInicio;
        Fim = novoFim;
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
}
