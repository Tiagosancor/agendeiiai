namespace Plataforma.Aplicacao.Financeiro;

/// <summary>Registro do pagamento por atendimento (seção 7) — um pagamento por agendamento.</summary>
public interface IGerenciadorPagamentos
{
    Task<Guid> RegistrarAsync(RegistrarPagamento dados, CancellationToken cancellationToken = default);

    Task<PagamentoResumo?> ObterPorAgendamentoAsync(Guid agendamentoId, CancellationToken cancellationToken = default);
}

public sealed record RegistrarPagamento(Guid AgendamentoId, decimal Valor, string Forma);

public sealed record PagamentoResumo(Guid Id, Guid AgendamentoId, decimal Valor, string Forma, DateTimeOffset CriadoEm);
