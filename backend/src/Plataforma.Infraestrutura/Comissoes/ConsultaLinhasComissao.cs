using Plataforma.Dominio.Agendamentos;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Comissoes;

/// <summary>Uma linha de serviço concluída com comissão gravada. Classe com init (não record posicional): o EF só traduz filtros sobre projeção feita por inicializador.</summary>
internal sealed class LinhaComissao
{
    public Guid LinhaId { get; init; }
    public Guid AgendamentoId { get; init; }
    public Guid ProfissionalId { get; init; }
    public Guid? ClienteId { get; init; }
    public DateTimeOffset Inicio { get; init; }
    public string Servico { get; init; } = string.Empty;
    public decimal ValorBase { get; init; }
    public decimal Percentual { get; init; }
    public decimal Comissao { get; init; }
}

/// <summary>
/// A mesma definição de "o que conta como comissão" para "Minhas comissões", o resumo do
/// Administrador e o fechamento por quinzena (seção 7): linha com comissão gravada, de atendimento
/// <c>Concluido</c>, pela data do atendimento (não da conclusão).
/// </summary>
internal static class ConsultaLinhasComissao
{
    public static IQueryable<LinhaComissao> Concluidas(PlataformaDbContext dbContext, DateTimeOffset inicioUtc, DateTimeOffset fimUtc) =>
        from s in dbContext.Set<AgendamentoServico>()
        join a in dbContext.Agendamentos on s.AgendamentoId equals a.Id
        where a.Status == StatusAgendamento.Concluido
            && s.ComissaoProfissionalId != null && s.ComissaoValor != null
            && a.Inicio >= inicioUtc && a.Inicio < fimUtc
        select new LinhaComissao
        {
            LinhaId = s.Id,
            AgendamentoId = a.Id,
            ProfissionalId = s.ComissaoProfissionalId!.Value,
            ClienteId = a.ClienteId,
            Inicio = a.Inicio,
            Servico = s.Nome,
            ValorBase = s.ComissaoValorBase!.Value,
            Percentual = s.ComissaoPercentual!.Value,
            Comissao = s.ComissaoValor!.Value,
        };

    /// <summary>Do início do dia <paramref name="de"/> (00:00) até o fim do dia <paramref name="ate"/> (23:59:59), no fuso do negócio.</summary>
    public static (DateTimeOffset InicioUtc, DateTimeOffset FimUtc) IntervaloUtc(DateOnly de, DateOnly ate, TimeZoneInfo fuso) =>
        (ConversorFusoHorario.ParaUtc(de, TimeOnly.MinValue, fuso).ToUniversalTime(),
         ConversorFusoHorario.ParaUtc(ate.AddDays(1), TimeOnly.MinValue, fuso).ToUniversalTime());
}
