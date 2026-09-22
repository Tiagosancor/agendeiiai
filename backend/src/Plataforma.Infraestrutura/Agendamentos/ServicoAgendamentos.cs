using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Cria e gerencia agendamentos (seção 8.2). A exclusion constraint do Postgres é a
/// garantia real contra sobreposição — este serviço só cuida do que ela não cobre
/// (expediente, almoço, bloqueios) e do ciclo de vida da reserva temporária, tudo numa
/// única transação (seção 8.2.3).
/// </summary>
public sealed class ServicoAgendamentos : IServicoAgendamentos
{
    private static readonly TimeSpan DuracaoDaReserva = TimeSpan.FromMinutes(10);

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IConsultaDisponibilidade _consultaDisponibilidade;

    public ServicoAgendamentos(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IConsultaDisponibilidade consultaDisponibilidade)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _consultaDisponibilidade = consultaDisponibilidade;
    }

    public Task<ResultadoAgendamento> CriarAsync(CriarAgendamento dados, CancellationToken cancellationToken = default) =>
        CriarInternoAsync(dados, confirmarDeImediato: true, cancellationToken);

    public Task<ResultadoAgendamento> CriarReservaAsync(CriarAgendamento dados, CancellationToken cancellationToken = default) =>
        CriarInternoAsync(dados, confirmarDeImediato: false, cancellationToken);

    private async Task<ResultadoAgendamento> CriarInternoAsync(
        CriarAgendamento dados, bool confirmarDeImediato, CancellationToken cancellationToken)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;

        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == negocioId, cancellationToken);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);

        var servicos = await _dbContext.Servicos.AsNoTracking()
            .Where(s => dados.ServicoIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        if (servicos.Count != dados.ServicoIds.Distinct().Count())
            return ResultadoAgendamento.ComErro("Um ou mais serviços não foram encontrados.");

        var overrides = await _dbContext.ProfissionalServicos.AsNoTracking()
            .Where(ps => ps.ProfissionalId == dados.ProfissionalId && dados.ServicoIds.Contains(ps.ServicoId))
            .ToListAsync(cancellationToken);

        // Serviços ocupam um único intervalo contínuo — soma das durações (seção 8.2.5).
        var itens = servicos.Select(s =>
        {
            var over = overrides.FirstOrDefault(o => o.ServicoId == s.Id);
            return new ItemServicoAgendamento(
                s.Id, s.Nome, over?.PrecoPersonalizado ?? s.Preco, over?.DuracaoPersonalizadaMinutos ?? s.DuracaoMinutos);
        }).ToList();

        var duracaoTotalMinutos = itens.Sum(i => i.DuracaoMinutos);
        var fim = dados.Inicio.AddMinutes(duracaoTotalMinutos);

        // EnableRetryOnFailure (seção 8.5.6) exige que transações manuais rodem dentro de
        // uma estratégia de execução — senão o EF Core lança na hora.
        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            // 1) Expira reservas vencidas deste profissional ANTES de checar disponibilidade
            // e inserir (seção 8.2.2) — a exclusion constraint não enxerga now().
            var agora = DateTimeOffset.UtcNow;
            await _dbContext.Agendamentos
                .Where(a => a.ProfissionalId == dados.ProfissionalId
                    && a.Status == StatusAgendamento.Reservado
                    && a.ReservadoAte < agora)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, StatusAgendamento.Expirado)
                    .SetProperty(a => a.ReservadoAte, (DateTimeOffset?)null), cancellationToken);

            // 2) Expediente e almoço — a exclusion constraint não cobre isso.
            var (diaLocal, horaInicioLocal) = ConversorFusoHorario.ParaLocal(dados.Inicio, fuso);
            var (_, horaFimLocal) = ConversorFusoHorario.ParaLocal(fim, fuso);
            var diaSemana = (DiaSemana)(int)diaLocal.DayOfWeek;

            var horariosDoDia = await _dbContext.HorariosTrabalho.AsNoTracking()
                .Where(h => h.ProfissionalId == dados.ProfissionalId && h.DiaSemana == diaSemana)
                .ToListAsync(cancellationToken);

            var cabeDentroDeUmIntervalo = horariosDoDia.Any(h => horaInicioLocal >= h.Inicio && horaFimLocal <= h.Fim);

            if (!cabeDentroDeUmIntervalo)
            {
                await transacao.RollbackAsync(cancellationToken);
                return ResultadoAgendamento.ComErro("Fora do expediente do profissional (ou cai no horário de almoço).");
            }

            // 3) Bloqueios/folgas.
            var temBloqueio = await _dbContext.BloqueiosAgenda.AsNoTracking()
                .AnyAsync(b => b.ProfissionalId == dados.ProfissionalId && b.InicioUtc < fim && b.FimUtc > dados.Inicio, cancellationToken);

            if (temBloqueio)
            {
                await transacao.RollbackAsync(cancellationToken);
                return ResultadoAgendamento.ComErro("O profissional está de folga ou bloqueado nesse horário.");
            }

            // 4) Insere — se outra requisição venceu a corrida pro mesmo intervalo, a
            // exclusion constraint recusa aqui (seção 8.2.1), nunca antes.
            var agendamento = confirmarDeImediato
                ? Agendamento.CriarConfirmado(negocioId, dados.ProfissionalId, dados.ClienteId, dados.Inicio, itens, dados.Observacoes)
                : Agendamento.CriarReserva(negocioId, dados.ProfissionalId, dados.ClienteId, dados.Inicio, itens, agora, DuracaoDaReserva);

            _dbContext.Agendamentos.Add(agendamento);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transacao.CommitAsync(cancellationToken);
                return ResultadoAgendamento.ComSucesso(agendamento.Id);
            }
            catch (DbUpdateException excecao) when (excecao.EhViolacaoDeExclusao())
            {
                await transacao.RollbackAsync(cancellationToken);

                var proximos = await _consultaDisponibilidade.ListarHorariosLivresAsync(
                    dados.ProfissionalId, diaLocal, duracaoTotalMinutos, cancellationToken);

                return ResultadoAgendamento.ComConflito(
                    "Esse horário acabou de ser preenchido. Escolha outro.", proximos.Take(5).ToList());
            }
        });
    }

    public async Task<ResultadoAgendamento> ConfirmarReservaAsync(Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var agendamento = await _dbContext.Agendamentos.FindAsync([agendamentoId], cancellationToken);

        if (agendamento is null)
            return ResultadoAgendamento.ComErro("Reserva não encontrada.");

        if (agendamento.Status != StatusAgendamento.Reservado || agendamento.ReservadoAte < DateTimeOffset.UtcNow)
            return ResultadoAgendamento.ComErro("Essa reserva já expirou ou não existe mais.");

        agendamento.ConfirmarReserva();
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ResultadoAgendamento.ComSucesso(agendamento.Id);
    }

    public async Task<bool> CancelarAsync(Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var agendamento = await _dbContext.Agendamentos.FindAsync([agendamentoId], cancellationToken);
        if (agendamento is null)
            return false;

        agendamento.Cancelar();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ResultadoAgendamento> MoverAsync(
        Guid agendamentoId, DateTimeOffset novoInicio, CancellationToken cancellationToken = default)
    {
        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var agendamento = await _dbContext.Agendamentos.FindAsync([agendamentoId], cancellationToken);
            if (agendamento is null)
            {
                await transacao.RollbackAsync(cancellationToken);
                return ResultadoAgendamento.ComErro("Agendamento não encontrado.");
            }

            var negocio = await _dbContext.Negocios.AsNoTracking()
                .FirstAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);
            var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);

            var duracaoTotalMinutos = agendamento.Servicos.Sum(s => s.DuracaoMinutos);
            var novoFim = novoInicio.AddMinutes(duracaoTotalMinutos);

            var (diaLocal, horaInicioLocal) = ConversorFusoHorario.ParaLocal(novoInicio, fuso);
            var (_, horaFimLocal) = ConversorFusoHorario.ParaLocal(novoFim, fuso);
            var diaSemana = (DiaSemana)(int)diaLocal.DayOfWeek;

            var horariosDoDia = await _dbContext.HorariosTrabalho.AsNoTracking()
                .Where(h => h.ProfissionalId == agendamento.ProfissionalId && h.DiaSemana == diaSemana)
                .ToListAsync(cancellationToken);

            if (!horariosDoDia.Any(h => horaInicioLocal >= h.Inicio && horaFimLocal <= h.Fim))
            {
                await transacao.RollbackAsync(cancellationToken);
                return ResultadoAgendamento.ComErro("Fora do expediente do profissional (ou cai no horário de almoço).");
            }

            var temBloqueio = await _dbContext.BloqueiosAgenda.AsNoTracking()
                .AnyAsync(b => b.ProfissionalId == agendamento.ProfissionalId
                    && b.InicioUtc < novoFim && b.FimUtc > novoInicio, cancellationToken);

            if (temBloqueio)
            {
                await transacao.RollbackAsync(cancellationToken);
                return ResultadoAgendamento.ComErro("O profissional está de folga ou bloqueado nesse horário.");
            }

            agendamento.Mover(novoInicio, novoFim);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transacao.CommitAsync(cancellationToken);
                return ResultadoAgendamento.ComSucesso(agendamento.Id);
            }
            catch (DbUpdateException excecao) when (excecao.EhViolacaoDeExclusao())
            {
                await transacao.RollbackAsync(cancellationToken);

                var proximos = await _consultaDisponibilidade.ListarHorariosLivresAsync(
                    agendamento.ProfissionalId, diaLocal, duracaoTotalMinutos, cancellationToken);

                return ResultadoAgendamento.ComConflito("Esse horário já está ocupado. Escolha outro.", proximos.Take(5).ToList());
            }
        });
    }

    public async Task<bool> MarcarConcluidoAsync(Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var agendamento = await _dbContext.Agendamentos.FindAsync([agendamentoId], cancellationToken);
        if (agendamento is null)
            return false;

        agendamento.MarcarConcluido();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> MarcarFaltouAsync(Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var agendamento = await _dbContext.Agendamentos.FindAsync([agendamentoId], cancellationToken);
        if (agendamento is null)
            return false;

        agendamento.MarcarFaltou();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AgendamentoResumo>> ListarAgendaDoDiaAsync(
        Guid profissionalId, DateOnly data, CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking()
            .FirstAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);

        var inicioDiaUtc = ConversorFusoHorario.ParaUtc(data, TimeOnly.MinValue, fuso);
        var fimDiaUtc = ConversorFusoHorario.ParaUtc(data.AddDays(1), TimeOnly.MinValue, fuso);

        var agendamentos = await _dbContext.Agendamentos.AsNoTracking()
            .Include(a => a.Servicos)
            .Where(a => a.ProfissionalId == profissionalId && a.Inicio < fimDiaUtc && a.Fim > inicioDiaUtc
                && a.Status != StatusAgendamento.Expirado)
            .OrderBy(a => a.Inicio)
            .ToListAsync(cancellationToken);

        if (agendamentos.Count == 0)
            return [];

        var clientes = await _dbContext.Clientes.AsNoTracking()
            .Where(c => agendamentos.Select(a => a.ClienteId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        return agendamentos.Select(a => new AgendamentoResumo(
            a.Id, a.ProfissionalId, a.ClienteId, clientes.TryGetValue(a.ClienteId, out var cliente) ? cliente.Nome : "—",
            a.Inicio, a.Fim, a.Status.ToString(), a.Observacoes,
            a.Servicos.Select(s => s.Nome).ToList(), a.Servicos.Sum(s => s.Preco))).ToList();
    }
}
