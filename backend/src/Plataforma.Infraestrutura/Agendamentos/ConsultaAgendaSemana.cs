using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>Monta a semana de um profissional com a mesma listagem do dia (<c>ListarAgendaDoDiaAsync</c>), dia a dia.</summary>
public sealed class ConsultaAgendaSemana : IConsultaAgendaSemana
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IServicoAgendamentos _agendamentos;

    public ConsultaAgendaSemana(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IServicoAgendamentos agendamentos)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _agendamentos = agendamentos;
    }

    public async Task<AgendaSemana?> ObterAsync(Guid profissionalId, DateOnly inicio, CancellationToken cancellationToken = default)
    {
        var profissional = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Id == profissionalId && !p.Excluido)
            .Select(p => new { p.Id, p.Nome })
            .FirstOrDefaultAsync(cancellationToken);
        if (profissional is null)
            return null;

        var fusoId = await _dbContext.Negocios.AsNoTracking()
            .Where(n => n.Id == _contextoNegocio.NegocioId)
            .Select(n => n.Fuso)
            .FirstAsync(cancellationToken);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(fusoId);

        var turnos = await _dbContext.HorariosTrabalho.AsNoTracking()
            .Where(h => h.ProfissionalId == profissionalId)
            .ToListAsync(cancellationToken);

        var inicioUtc = ConversorFusoHorario.ParaUtc(inicio, TimeOnly.MinValue, fuso);
        var fimUtc = ConversorFusoHorario.ParaUtc(inicio.AddDays(7), TimeOnly.MinValue, fuso);
        var bloqueios = await _dbContext.BloqueiosAgenda.AsNoTracking()
            .Where(b => b.ProfissionalId == profissionalId && b.InicioUtc < fimUtc && b.FimUtc > inicioUtc)
            .ToListAsync(cancellationToken);

        var dias = new List<DiaAgendaSemana>(7);
        for (var i = 0; i < 7; i++)
        {
            var dia = inicio.AddDays(i);
            var diaSemana = (DiaSemana)(int)dia.DayOfWeek;
            var turnosDoDia = turnos.Where(t => t.DiaSemana == diaSemana).OrderBy(t => t.Inicio).Select(t => $"{Hora(t.Inicio)}–{Hora(t.Fim)}").ToList();

            var inicioDia = ConversorFusoHorario.ParaUtc(dia, TimeOnly.MinValue, fuso);
            var fimDia = ConversorFusoHorario.ParaUtc(dia.AddDays(1), TimeOnly.MinValue, fuso);
            var bloqueiosDoDia = bloqueios
                .Where(b => b.InicioUtc < fimDia && b.FimUtc > inicioDia)
                .OrderBy(b => b.InicioUtc)
                .Select(b => DescreverBloqueio(b.InicioUtc, b.FimUtc, b.Motivo, inicioDia, fimDia, fuso))
                .ToList();

            var agendamentos = await _agendamentos.ListarAgendaDoDiaAsync(profissionalId, dia, cancellationToken);
            dias.Add(new DiaAgendaSemana(dia, turnosDoDia.Count == 0, turnosDoDia, bloqueiosDoDia, agendamentos));
        }

        return new AgendaSemana(profissional.Id, profissional.Nome, inicio, dias);
    }

    private static string DescreverBloqueio(
        DateTimeOffset inicioUtc, DateTimeOffset fimUtc, string? motivo, DateTimeOffset inicioDia, DateTimeOffset fimDia, TimeZoneInfo fuso)
    {
        var diaTodo = inicioUtc <= inicioDia && fimUtc >= fimDia;
        var faixa = diaTodo
            ? "o dia todo"
            : $"{Hora(ConversorFusoHorario.ParaLocal(inicioUtc < inicioDia ? inicioDia : inicioUtc, fuso).Hora)}–"
              + (fimUtc >= fimDia ? "24:00" : Hora(ConversorFusoHorario.ParaLocal(fimUtc, fuso).Hora));
        return string.IsNullOrWhiteSpace(motivo) ? $"Bloqueado {faixa}" : $"Bloqueado {faixa} ({motivo})";
    }

    private static string Hora(TimeOnly hora) => hora.ToString("HH:mm", CultureInfo.InvariantCulture);
}

public sealed class AgendaDoProfissionalLogado : IAgendaDoProfissionalLogado
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IUsuarioAtual _usuarioAtual;

    public AgendaDoProfissionalLogado(PlataformaDbContext dbContext, IUsuarioAtual usuarioAtual)
    {
        _dbContext = dbContext;
        _usuarioAtual = usuarioAtual;
    }

    public async Task<Guid?> MeuProfissionalAsync(CancellationToken cancellationToken = default)
    {
        if (_usuarioAtual.UsuarioId is not { } usuarioId)
            return null;

        return await (
            from u in _dbContext.Usuarios
            join p in _dbContext.Profissionais on u.ProfissionalId equals p.Id
            where u.Id == usuarioId && !p.Excluido
            select (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> EhMeuAsync(Guid agendamentoId, CancellationToken cancellationToken = default) =>
        await MeuProfissionalAsync(cancellationToken) is { } profissionalId
        && await _dbContext.Agendamentos.AnyAsync(a => a.Id == agendamentoId && a.ProfissionalId == profissionalId, cancellationToken);
}
