namespace Plataforma.Aplicacao.Financeiro;

/// <summary>Faturamento por período, profissional e serviço, e painel simples do mês (seção 7) — o "mês" é só este resumo filtrado no 1º/último dia do mês corrente pelo chamador.</summary>
public interface IServicoFinanceiro
{
    Task<ResumoFinanceiro> ObterResumoAsync(FiltroFinanceiro filtro, CancellationToken cancellationToken = default);
}

public sealed record FiltroFinanceiro(DateOnly Inicio, DateOnly Fim, Guid? ProfissionalId = null, Guid? ServicoId = null);

public sealed record ResumoFinanceiro(
    decimal Total, int QuantidadeAtendimentos,
    IReadOnlyList<FaturamentoPorProfissional> PorProfissional,
    IReadOnlyList<FaturamentoPorServico> PorServico);

public sealed record FaturamentoPorProfissional(Guid ProfissionalId, string NomeProfissional, decimal Total, int Quantidade);

public sealed record FaturamentoPorServico(Guid ServicoId, string NomeServico, decimal Total, int Quantidade);
