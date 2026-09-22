using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Fidelidade;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Cupons;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
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
    private readonly INotificador _notificador;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly OpcoesMarca _opcoesMarca;
    private readonly IGerenciadorFidelidade _gerenciadorFidelidade;

    public ServicoAgendamentos(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IConsultaDisponibilidade consultaDisponibilidade,
        INotificador notificador, IServicoTokenPublico servicoToken, IOptions<OpcoesMarca> opcoesMarca,
        IGerenciadorFidelidade gerenciadorFidelidade)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _consultaDisponibilidade = consultaDisponibilidade;
        _notificador = notificador;
        _servicoToken = servicoToken;
        _opcoesMarca = opcoesMarca.Value;
        _gerenciadorFidelidade = gerenciadorFidelidade;
    }

    public async Task<ResultadoAgendamento> CriarAsync(CriarAgendamento dados, CancellationToken cancellationToken = default)
    {
        var resultado = await CriarInternoAsync(dados, confirmarDeImediato: true, cancellationToken);

        if (resultado.Sucesso)
            await NotificarProfissionalAsync(resultado.AgendamentoId!.Value, EventoAgendamentoProfissional.Novo, cancellationToken);

        return resultado;
    }

    public Task<ResultadoAgendamento> CriarReservaAsync(CriarAgendamento dados, CancellationToken cancellationToken = default) =>
        CriarInternoAsync(dados, confirmarDeImediato: false, cancellationToken);

    public Task<ResultadoAgendamento> CriarReservaPublicaAsync(CriarReservaPublica dados, CancellationToken cancellationToken = default) =>
        CriarInternoAsync(
            new CriarAgendamento(dados.ProfissionalId, null, dados.ServicoIds, dados.Inicio), confirmarDeImediato: false, cancellationToken);

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
                ? Agendamento.CriarConfirmado(
                    negocioId, dados.ProfissionalId,
                    dados.ClienteId ?? throw new InvalidOperationException("Um agendamento confirmado de imediato precisa de um cliente."),
                    dados.Inicio, itens, dados.Observacoes)
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

    public async Task<ResultadoAgendamento> ConfirmarReservaPublicaAsync(
        ConfirmarReservaPublica dados, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var telefone = TelefoneE164.Criar(dados.TelefoneCliente);

        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        var resultado = await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var agendamento = await _dbContext.Agendamentos
                .Include(a => a.Servicos)
                .FirstOrDefaultAsync(a => a.Id == dados.AgendamentoId, cancellationToken);

            if (agendamento is null || agendamento.Status != StatusAgendamento.Reservado || agendamento.ReservadoAte < DateTimeOffset.UtcNow)
            {
                await transacao.RollbackAsync(cancellationToken);
                return ResultadoAgendamento.ComErro("Essa reserva já expirou ou não existe mais.");
            }

            // Identificação automática do cliente pelo telefone, mesma transação (seção 8.1.4).
            var (clienteId, nomeInformado) = await IdentificarOuCriarClienteAsync(
                negocioId, telefone, dados.NomeCliente, dados.EmailCliente, cancellationToken);

            agendamento.VincularCliente(clienteId);
            agendamento.DefinirNomeInformado(nomeInformado);
            agendamento.DefinirObservacoes(dados.Observacoes);

            // Cupom revalidado no servidor (seção 6.2.4/7) — se inválido, ignora o desconto
            // sem falhar o agendamento (o passo "Aplicar" do assistente já avisou o cliente antes).
            if (!string.IsNullOrWhiteSpace(dados.CodigoCupom))
            {
                var cupom = await _dbContext.Cupons.FirstOrDefaultAsync(
                    c => c.NegocioId == negocioId && c.Codigo == dados.CodigoCupom.Trim().ToUpperInvariant(), cancellationToken);

                if (cupom is not null)
                {
                    var totalServicos = agendamento.Servicos.Sum(s => s.Preco);
                    var servicoIds = agendamento.Servicos.Select(s => s.ServicoId).ToHashSet();
                    var resultadoCupom = cupom.TentarAplicar(totalServicos, servicoIds, DateTimeOffset.UtcNow);

                    if (resultadoCupom.Sucesso)
                    {
                        agendamento.AplicarCupom(cupom.Id, resultadoCupom.Desconto);
                        cupom.RegistrarUso();
                    }
                }
            }

            agendamento.RegistrarConsentimento(DateTimeOffset.UtcNow, dados.IpCliente, VersaoTermos.Atual);
            agendamento.ConfirmarReserva();
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);

            return ResultadoAgendamento.ComSucesso(agendamento.Id);
        });

        if (resultado.Sucesso)
            await NotificarConfirmacaoAsync(resultado.AgendamentoId!.Value, cancellationToken);

        return resultado;
    }

    /// <summary>Busca o cliente por (negócio, telefone) — índice único (seção 8.1.4) — ou cria; nunca sobrescreve dado já existente, só preenche o vazio.</summary>
    private async Task<(Guid ClienteId, string? NomeInformado)> IdentificarOuCriarClienteAsync(
        Guid negocioId, TelefoneE164 telefone, string nome, string? email, CancellationToken cancellationToken)
    {
        var existente = await _dbContext.Clientes.FirstOrDefaultAsync(
            c => c.NegocioId == negocioId && c.Telefone == telefone, cancellationToken);

        if (existente is not null)
            return (existente.Id, VincularDadosExistentes(existente, nome, email));

        var novoCliente = Cliente.Criar(negocioId, nome, telefone, OrigemCliente.LinkPublico, email);
        _dbContext.Clientes.Add(novoCliente);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return (novoCliente.Id, null);
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            // Duas requisições simultâneas com o mesmo telefone (seção 8.1.4) — a outra venceu.
            _dbContext.Entry(novoCliente).State = EntityState.Detached;

            var clienteDaCorrida = await _dbContext.Clientes.FirstAsync(
                c => c.NegocioId == negocioId && c.Telefone == telefone, cancellationToken);

            return (clienteDaCorrida.Id, VincularDadosExistentes(clienteDaCorrida, nome, email));
        }
    }

    private static string? VincularDadosExistentes(Cliente existente, string nome, string? email)
    {
        existente.PreencherEmailSeVazio(email);
        var nomeNormalizado = nome.Trim();
        return string.Equals(existente.Nome, nomeNormalizado, StringComparison.OrdinalIgnoreCase) ? null : nomeNormalizado;
    }

    private async Task NotificarConfirmacaoAsync(Guid agendamentoId, CancellationToken cancellationToken)
    {
        var agendamento = await _dbContext.Agendamentos.AsNoTracking()
            .Include(a => a.Servicos)
            .FirstAsync(a => a.Id == agendamentoId, cancellationToken);

        var cliente = await _dbContext.Clientes.AsNoTracking().FirstAsync(c => c.Id == agendamento.ClienteId, cancellationToken);
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == agendamento.NegocioId, cancellationToken);

        var tokenAgendamento = _servicoToken.GerarTokenAgendamento(negocio.Id, agendamento.Id);
        var linkCancelar = ConstrutorUrlPublica.Construir(_opcoesMarca, negocio.Slug.Valor, $"/agendamentos/{tokenAgendamento}");

        await _notificador.EnviarConfirmacaoAgendamentoAsync(new DadosNotificacaoAgendamento(
            cliente.Nome, cliente.Email, cliente.Telefone, agendamento.Inicio, agendamento.Fim,
            agendamento.Servicos.Select(s => s.Nome).ToList(), agendamento.Total, linkCancelar, linkCancelar), cancellationToken);

        await NotificarProfissionalAsync(agendamento, cliente.Nome, EventoAgendamentoProfissional.Novo, cancellationToken);
    }

    /// <summary>Novo/remarcado/cancelado ao profissional (seção 9, Sprint 4) — nunca falha o fluxo principal se der errado (ver <c>Notificador</c>).</summary>
    private async Task NotificarProfissionalAsync(Guid agendamentoId, EventoAgendamentoProfissional evento, CancellationToken cancellationToken)
    {
        var agendamento = await _dbContext.Agendamentos.AsNoTracking()
            .Include(a => a.Servicos)
            .FirstOrDefaultAsync(a => a.Id == agendamentoId, cancellationToken);

        if (agendamento is null)
            return;

        var nomeCliente = "—";
        if (agendamento.ClienteId is not null)
        {
            var cliente = await _dbContext.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == agendamento.ClienteId, cancellationToken);
            nomeCliente = cliente?.Nome ?? agendamento.NomeInformado ?? "—";
        }

        await NotificarProfissionalAsync(agendamento, nomeCliente, evento, cancellationToken);
    }

    private async Task NotificarProfissionalAsync(
        Agendamento agendamento, string nomeCliente, EventoAgendamentoProfissional evento, CancellationToken cancellationToken)
    {
        var profissional = await _dbContext.Profissionais.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == agendamento.ProfissionalId, cancellationToken);

        if (profissional is null)
            return;

        await _notificador.EnviarNotificacaoProfissionalAsync(new DadosNotificacaoProfissional(
            evento, profissional.Email, nomeCliente, agendamento.Inicio, agendamento.Fim,
            agendamento.Servicos.Select(s => s.Nome).ToList(), agendamento.Observacoes), cancellationToken);
    }

    public async Task<ResultadoPreVisualizacaoCupom> PreVisualizarCupomAsync(
        Guid agendamentoId, string codigoCupom, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;

        var agendamento = await _dbContext.Agendamentos.AsNoTracking()
            .Include(a => a.Servicos)
            .FirstOrDefaultAsync(a => a.Id == agendamentoId, cancellationToken);

        if (agendamento is null)
            return new ResultadoPreVisualizacaoCupom(false, MensagemErro: "Reserva não encontrada.");

        var cupom = await _dbContext.Cupons.AsNoTracking()
            .FirstOrDefaultAsync(c => c.NegocioId == negocioId && c.Codigo == codigoCupom.Trim().ToUpperInvariant(), cancellationToken);

        if (cupom is null)
            return new ResultadoPreVisualizacaoCupom(false, MensagemErro: "Cupom não encontrado.");

        var totalServicos = agendamento.Servicos.Sum(s => s.Preco);
        var servicoIds = agendamento.Servicos.Select(s => s.ServicoId).ToHashSet();
        var resultado = cupom.TentarAplicar(totalServicos, servicoIds, DateTimeOffset.UtcNow);

        return resultado.Sucesso
            ? new ResultadoPreVisualizacaoCupom(true, resultado.Desconto)
            : new ResultadoPreVisualizacaoCupom(false, MensagemErro: resultado.MensagemErro);
    }

    public async Task<bool> CancelarAsync(Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var agendamento = await _dbContext.Agendamentos.FindAsync([agendamentoId], cancellationToken);
        if (agendamento is null)
            return false;

        agendamento.Cancelar();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await NotificarProfissionalAsync(agendamentoId, EventoAgendamentoProfissional.Cancelado, cancellationToken);
        return true;
    }

    public async Task<ResultadoAgendamento> MoverAsync(
        Guid agendamentoId, DateTimeOffset novoInicio, CancellationToken cancellationToken = default)
    {
        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        var resultado = await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            // FindAsync não carrega Servicos (não aceita Include) — sem isso, duracaoTotalMinutos
            // sempre dava 0 e Agendamento.Mover sempre lançava "fim precisa ser depois do início".
            // Bug pré-existente, nunca pego antes porque não havia teste de "mover com sucesso".
            var agendamento = await _dbContext.Agendamentos
                .Include(a => a.Servicos)
                .FirstOrDefaultAsync(a => a.Id == agendamentoId, cancellationToken);
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

        if (resultado.Sucesso)
            await NotificarProfissionalAsync(agendamentoId, EventoAgendamentoProfissional.Remarcado, cancellationToken);

        return resultado;
    }

    public async Task<bool> MarcarConcluidoAsync(Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var agendamento = await _dbContext.Agendamentos.FindAsync([agendamentoId], cancellationToken);
        if (agendamento is null)
            return false;

        agendamento.MarcarConcluido();
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Selo do cartão de fidelidade (seção 7) — não faz nada se o negócio não tiver
        // programa ativo (ver GerenciadorFidelidade.RegistrarSeloAsync).
        if (agendamento.ClienteId is not null)
            await _gerenciadorFidelidade.RegistrarSeloAsync(agendamento.ClienteId.Value, agendamento.Id, cancellationToken);

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

        // ClienteId é nulo enquanto uma reserva do assistente público ainda não passou
        // pela identificação do cliente (seção 8.1.4) — aparece como "Reservando..." na agenda.
        var clienteIds = agendamentos.Where(a => a.ClienteId is not null).Select(a => a.ClienteId!.Value).ToList();
        var clientes = await _dbContext.Clientes.AsNoTracking()
            .Where(c => clienteIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        return agendamentos.Select(a => new AgendamentoResumo(
            a.Id, a.ProfissionalId, a.ClienteId,
            a.ClienteId is not null && clientes.TryGetValue(a.ClienteId.Value, out var cliente) ? cliente.Nome : "Reservando...",
            a.Inicio, a.Fim, a.Status.ToString(), a.Observacoes,
            a.Servicos.Select(s => s.Nome).ToList(), a.Servicos.Sum(s => s.Preco))).ToList();
    }

    public async Task<DetalhePublicoAgendamento?> ObterDetalhePublicoAsync(Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var agendamento = await _dbContext.Agendamentos.AsNoTracking()
            .Include(a => a.Servicos)
            .FirstOrDefaultAsync(a => a.Id == agendamentoId, cancellationToken);

        if (agendamento is null)
            return null;

        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == agendamento.NegocioId, cancellationToken);
        var local = string.Join(", ", new[] { negocio.Endereco.Rua, negocio.Endereco.Numero, negocio.Endereco.Bairro, negocio.Endereco.Cidade }
            .Where(parte => !string.IsNullOrWhiteSpace(parte)));

        return new DetalhePublicoAgendamento(
            agendamento.Id, negocio.NomeExibido, local, agendamento.Inicio, agendamento.Fim,
            agendamento.Servicos.Select(s => s.Nome).ToList(), agendamento.Total, agendamento.Status.ToString());
    }
}
