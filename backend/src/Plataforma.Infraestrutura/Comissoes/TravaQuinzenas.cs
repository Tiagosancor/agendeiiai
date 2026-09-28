using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Comissoes;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Comissoes;

/// <summary>
/// Quinzena fechada trava os atendimentos dela (seção 7). Duas peças:
/// <list type="bullet">
/// <item><see cref="TravarNegocioAsync"/>: trava de transação por negócio, tomada por quem fecha ou
/// reabre uma quinzena e por quem conclui, reabre ou cancela um atendimento — um atendimento
/// concluído no mesmo instante do fechamento não escapa do total nem entra depois dele.</item>
/// <item><see cref="AtendimentoTravadoAsync"/>: o atendimento é de um profissional que já tem
/// fechamento numa quinzena fechada que contém o dia dele (no fuso do negócio). Profissional sem
/// acerto por quinzena não entra no fechamento, então nunca fica travado.</item>
/// </list>
/// </summary>
public sealed class TravaQuinzenas
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public TravaQuinzenas(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    /// <summary>Precisa estar dentro de uma transação explícita; é liberada no commit/rollback.</summary>
    public Task TravarNegocioAsync(CancellationToken cancellationToken) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"quinzenas:" + _contextoNegocio.NegocioId}, 0))", cancellationToken);

    public async Task<bool> AtendimentoTravadoAsync(Guid profissionalId, DateTimeOffset inicioUtc, CancellationToken cancellationToken)
    {
        var dia = await DiaLocalAsync(inicioUtc, cancellationToken);

        return await (
            from f in _dbContext.FechamentosComissao
            join p in _dbContext.PeriodosComissao on f.PeriodoComissaoId equals p.Id
            where p.Estado == EstadoPeriodoComissao.Fechada && f.ProfissionalId == profissionalId && p.Inicio <= dia && p.Fim >= dia
            select f.Id).AnyAsync(cancellationToken);
    }

    public async Task GarantirNaoTravadoAsync(Guid profissionalId, DateTimeOffset inicioUtc, CancellationToken cancellationToken)
    {
        if (await AtendimentoTravadoAsync(profissionalId, inicioUtc, cancellationToken))
            throw new QuinzenaFechadaException();
    }

    public async Task<TimeZoneInfo> FusoAsync(CancellationToken cancellationToken)
    {
        var fusoId = await _dbContext.Negocios.AsNoTracking()
            .Where(n => n.Id == _contextoNegocio.NegocioId)
            .Select(n => n.Fuso)
            .FirstAsync(cancellationToken);
        return TimeZoneInfo.FindSystemTimeZoneById(fusoId);
    }

    private async Task<DateOnly> DiaLocalAsync(DateTimeOffset instanteUtc, CancellationToken cancellationToken) =>
        ConversorFusoHorario.ParaLocal(instanteUtc, await FusoAsync(cancellationToken)).Dia;
}
