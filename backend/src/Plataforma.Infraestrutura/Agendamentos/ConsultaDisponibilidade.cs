using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Calcula os horários livres de um profissional num dia (seção 6.2.2): parte do
/// expediente (seção 7), subtrai bloqueios e agendamentos ativos, e oferece o que sobrar
/// na grade de 15 min — só quando a duração total dos serviços couber inteira.
/// </summary>
public sealed class ConsultaDisponibilidade : IConsultaDisponibilidade
{
    private static readonly TimeSpan TamanhoDaGrade = TimeSpan.FromMinutes(15);

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public ConsultaDisponibilidade(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<DateTimeOffset>> ListarHorariosLivresAsync(
        Guid profissionalId, DateOnly data, int duracaoTotalMinutos, CancellationToken cancellationToken = default)
    {
        if (duracaoTotalMinutos <= 0)
            return [];

        var negocio = await _dbContext.Negocios.AsNoTracking()
            .FirstAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);

        var diaSemana = (DiaSemana)(int)data.DayOfWeek;

        var horariosDoDia = await _dbContext.HorariosTrabalho.AsNoTracking()
            .Where(h => h.ProfissionalId == profissionalId && h.DiaSemana == diaSemana)
            .OrderBy(h => h.Inicio)
            .ToListAsync(cancellationToken);

        if (horariosDoDia.Count == 0)
            return [];

        // Janela UTC generosa (com folga de 24h pra cada lado) — cobre qualquer fuso sem
        // perder agendamento/bloqueio que intersecte o dia local. O índice em
        // (profissional_id, inicio) mantém isso rápido mesmo com muitos agendamentos.
        var inicioDiaUtc = ConversorFusoHorario.ParaUtc(data, TimeOnly.MinValue, fuso).AddHours(-24);
        var fimDiaUtc = ConversorFusoHorario.ParaUtc(data, TimeOnly.MaxValue, fuso).AddHours(24);

        var ocupados = await _dbContext.Agendamentos.AsNoTracking()
            .Where(a => a.ProfissionalId == profissionalId
                && a.Inicio < fimDiaUtc && a.Fim > inicioDiaUtc
                && (a.Status == StatusAgendamento.Reservado
                    || a.Status == StatusAgendamento.Agendado
                    || a.Status == StatusAgendamento.Concluido))
            .Select(a => new { a.Inicio, a.Fim })
            .ToListAsync(cancellationToken);

        var bloqueios = await _dbContext.BloqueiosAgenda.AsNoTracking()
            .Where(b => b.ProfissionalId == profissionalId && b.InicioUtc < fimDiaUtc && b.FimUtc > inicioDiaUtc)
            .Select(b => new { b.InicioUtc, b.FimUtc })
            .ToListAsync(cancellationToken);

        var segmentosLivres = horariosDoDia
            .Select(h => (
                Inicio: ConversorFusoHorario.ParaUtc(data, h.Inicio, fuso),
                Fim: ConversorFusoHorario.ParaUtc(data, h.Fim, fuso)))
            .ToList();

        foreach (var ocupado in ocupados)
            segmentosLivres = SubtrairIntervalo(segmentosLivres, ocupado.Inicio, ocupado.Fim);

        foreach (var bloqueio in bloqueios)
            segmentosLivres = SubtrairIntervalo(segmentosLivres, bloqueio.InicioUtc, bloqueio.FimUtc);

        var duracao = TimeSpan.FromMinutes(duracaoTotalMinutos);
        var agora = DateTimeOffset.UtcNow;
        var horarios = new List<DateTimeOffset>();

        foreach (var (inicioSegmento, fimSegmento) in segmentosLivres)
        {
            var cursor = ArredondarParaGrade(inicioSegmento, fuso);

            while (cursor + duracao <= fimSegmento)
            {
                if (cursor > agora)
                    horarios.Add(cursor);

                cursor += TamanhoDaGrade;
            }
        }

        return horarios.OrderBy(h => h).ToList();
    }

    /// <summary>Arredonda para cima até o próximo múltiplo de 15 min do relógio LOCAL (ex.: 09:07 vira 09:15) — a grade é sempre "redonda" pra quem vê, não um deslocamento arbitrário.</summary>
    private static DateTimeOffset ArredondarParaGrade(DateTimeOffset instante, TimeZoneInfo fuso)
    {
        var (dia, hora) = ConversorFusoHorario.ParaLocal(instante, fuso);
        var minutosDesdeMeiaNoite = hora.Hour * 60 + hora.Minute;
        var resto = minutosDesdeMeiaNoite % 15;

        if (resto == 0 && hora.Second == 0 && hora.Millisecond == 0)
            return instante;

        var minutosArredondados = minutosDesdeMeiaNoite - resto + 15;

        return minutosArredondados >= 24 * 60
            ? ConversorFusoHorario.ParaUtc(dia.AddDays(1), TimeOnly.MinValue, fuso)
            : ConversorFusoHorario.ParaUtc(dia, new TimeOnly(0, 0).Add(TimeSpan.FromMinutes(minutosArredondados)), fuso);
    }

    private static List<(DateTimeOffset Inicio, DateTimeOffset Fim)> SubtrairIntervalo(
        List<(DateTimeOffset Inicio, DateTimeOffset Fim)> segmentos, DateTimeOffset inicioOcupado, DateTimeOffset fimOcupado)
    {
        var resultado = new List<(DateTimeOffset, DateTimeOffset)>();

        foreach (var (inicio, fim) in segmentos)
        {
            if (fimOcupado <= inicio || inicioOcupado >= fim)
            {
                resultado.Add((inicio, fim));
                continue;
            }

            if (inicioOcupado > inicio)
                resultado.Add((inicio, inicioOcupado));

            if (fimOcupado < fim)
                resultado.Add((fimOcupado, fim));
        }

        return resultado;
    }
}
