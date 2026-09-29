using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Monta a grade do dia (seção 7): as linhas vão do primeiro turno ao último do dia entre todos os
/// profissionais ativos (e cobrem qualquer agendamento fora disso, como um forçado). Ocupado vale para os
/// mesmos status que ocupam horário na exclusion constraint e na disponibilidade.
/// </summary>
public sealed class ConsultaGradeAgenda : IConsultaGradeAgenda
{
    private const int MinutosDaGrade = 15;
    private static readonly TimeOnly InicioPadrao = new(8, 0);
    private static readonly TimeOnly FimPadrao = new(18, 0);

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public ConsultaGradeAgenda(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<GradeAgenda> ObterAsync(DateOnly data, CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);
        var diaSemana = (DiaSemana)(int)data.DayOfWeek;
        var inicioDia = ConversorFusoHorario.ParaUtc(data, TimeOnly.MinValue, fuso);
        var fimDia = ConversorFusoHorario.ParaUtc(data.AddDays(1), TimeOnly.MinValue, fuso);
        var agora = DateTimeOffset.UtcNow;

        var profissionais = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Ativo && !p.Excluido)
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome })
            .ToListAsync(cancellationToken);
        var ids = profissionais.Select(p => p.Id).ToList();

        var turnos = (await _dbContext.HorariosTrabalho.AsNoTracking()
                .Where(h => ids.Contains(h.ProfissionalId) && h.DiaSemana == diaSemana)
                .ToListAsync(cancellationToken))
            .OrderBy(h => h.Inicio)
            .ToList();

        var bloqueios = await _dbContext.BloqueiosAgenda.AsNoTracking()
            .Where(b => ids.Contains(b.ProfissionalId) && b.InicioUtc < fimDia && b.FimUtc > inicioDia)
            .ToListAsync(cancellationToken);

        var agendamentos = await _dbContext.Agendamentos.AsNoTracking()
            .Include(a => a.Servicos)
            .Where(a => ids.Contains(a.ProfissionalId) && a.Inicio < fimDia && a.Fim > inicioDia
                && (a.Status == StatusAgendamento.Agendado
                    || a.Status == StatusAgendamento.EmAtendimento
                    || a.Status == StatusAgendamento.Concluido
                    || (a.Status == StatusAgendamento.Reservado && a.ReservadoAte >= agora)))
            .OrderBy(a => a.Inicio)
            .ToListAsync(cancellationToken);

        var clienteIds = agendamentos.Where(a => a.ClienteId is not null).Select(a => a.ClienteId!.Value).Distinct().ToList();
        var clientes = clienteIds.Count == 0
            ? []
            : await _dbContext.Clientes.AsNoTracking()
                .Where(c => clienteIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Nome, cancellationToken);

        var linhas = MontarLinhas(data, fuso, turnos.Select(t => (t.Inicio, t.Fim)), agendamentos.Select(a => (a.Inicio, a.Fim)), inicioDia, fimDia);

        var colunas = profissionais.Select(profissional =>
        {
            var turnosDele = turnos.Where(t => t.ProfissionalId == profissional.Id).ToList();
            var bloqueiosDele = bloqueios.Where(b => b.ProfissionalId == profissional.Id).ToList();
            var agendamentosDele = agendamentos.Where(a => a.ProfissionalId == profissional.Id).ToList();

            var celulas = linhas.Select(linha =>
            {
                var fimCelula = linha.Inicio.AddMinutes(MinutosDaGrade);

                var ocupante = agendamentosDele.FirstOrDefault(a => a.Inicio < fimCelula && a.Fim > linha.Inicio);
                if (ocupante is not null)
                    return new CelulaGrade(linha.Rotulo, linha.Inicio, EstadoCelulaGrade.Ocupado, ocupante.Id);

                var bloqueio = bloqueiosDele.FirstOrDefault(b => b.InicioUtc < fimCelula && b.FimUtc > linha.Inicio);
                if (bloqueio is not null)
                    return new CelulaGrade(linha.Rotulo, linha.Inicio, EstadoCelulaGrade.Bloqueio, Descricao: bloqueio.Motivo);

                if (turnosDele.Count == 0)
                    return new CelulaGrade(linha.Rotulo, linha.Inicio, EstadoCelulaGrade.Folga);

                // A célula inteira precisa caber num turno; a última célula do dia pode ir até a meia-noite.
                var horaFim = linha.Hora.AddMinutes(MinutosDaGrade);
                var horaFimComparavel = horaFim == TimeOnly.MinValue ? TimeOnly.MaxValue : horaFim;
                if (turnosDele.Any(t => linha.Hora >= t.Inicio && horaFimComparavel <= t.Fim))
                    return new CelulaGrade(linha.Rotulo, linha.Inicio, EstadoCelulaGrade.Livre);

                var entreTurnos = linha.Hora >= turnosDele.Min(t => t.Inicio) && horaFimComparavel <= turnosDele.Max(t => t.Fim);
                return new CelulaGrade(linha.Rotulo, linha.Inicio, entreTurnos ? EstadoCelulaGrade.Almoco : EstadoCelulaGrade.ForaDoExpediente);
            }).ToList();

            // De folga: sem expediente no dia, ou com todo o expediente bloqueado.
            var deFolga = turnosDele.Count == 0 || turnosDele.All(t =>
            {
                var inicioTurno = ConversorFusoHorario.ParaUtc(data, t.Inicio, fuso);
                var fimTurno = ConversorFusoHorario.ParaUtc(data, t.Fim, fuso);
                return bloqueiosDele.Any(b => b.InicioUtc <= inicioTurno && b.FimUtc >= fimTurno);
            });

            var resumo = agendamentosDele.Select(a => new AgendamentoNaGrade(
                a.Id,
                a.ClienteId is not null && clientes.TryGetValue(a.ClienteId.Value, out var nome) ? nome : "Reservando...",
                a.Servicos.Select(s => s.Nome).ToList(), a.Status.ToString(), a.Forcado, a.Inicio, a.Fim)).ToList();

            return new ColunaGrade(profissional.Id, profissional.Nome, deFolga, celulas, resumo);
        }).ToList();

        return new GradeAgenda(data, linhas.Select(l => l.Rotulo).ToList(), colunas);
    }

    private sealed record Linha(TimeOnly Hora, string Rotulo, DateTimeOffset Inicio);

    /// <summary>Do início do primeiro turno ao fim do último (padrão 08:00–18:00), alargado para caber os agendamentos do dia.</summary>
    private static List<Linha> MontarLinhas(
        DateOnly data, TimeZoneInfo fuso, IEnumerable<(TimeOnly Inicio, TimeOnly Fim)> turnos,
        IEnumerable<(DateTimeOffset Inicio, DateTimeOffset Fim)> agendamentos, DateTimeOffset inicioDia, DateTimeOffset fimDia)
    {
        var listaTurnos = turnos.ToList();
        var primeiro = listaTurnos.Count == 0 ? MinutosDoDia(InicioPadrao) : listaTurnos.Min(t => MinutosDoDia(t.Inicio));
        var ultimo = listaTurnos.Count == 0 ? MinutosDoDia(FimPadrao) : listaTurnos.Max(t => MinutosDoDia(t.Fim));

        foreach (var (inicio, fim) in agendamentos)
        {
            primeiro = Math.Min(primeiro, inicio <= inicioDia ? 0 : MinutosDoDia(ConversorFusoHorario.ParaLocal(inicio, fuso).Hora));
            ultimo = Math.Max(ultimo, fim >= fimDia ? 24 * 60 : MinutosDoDia(ConversorFusoHorario.ParaLocal(fim, fuso).Hora));
        }

        primeiro -= primeiro % MinutosDaGrade;
        ultimo = Math.Min(24 * 60, ultimo + (MinutosDaGrade - ultimo % MinutosDaGrade) % MinutosDaGrade);

        var linhas = new List<Linha>();
        for (var minuto = primeiro; minuto < ultimo; minuto += MinutosDaGrade)
        {
            var hora = new TimeOnly(minuto / 60, minuto % 60);
            // Em UTC de fato (offset 0): é o mesmo formato dos horários livres, e a tela compara os dois como texto.
            linhas.Add(new Linha(hora, hora.ToString("HH:mm", CultureInfo.InvariantCulture), ConversorFusoHorario.ParaUtc(data, hora, fuso).ToUniversalTime()));
        }

        return linhas;
    }

    private static int MinutosDoDia(TimeOnly hora) => hora.Hour * 60 + hora.Minute;
}
