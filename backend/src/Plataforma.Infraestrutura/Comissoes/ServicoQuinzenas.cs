using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comissoes;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Comissoes;

/// <summary>
/// Fechamento de comissões por quinzena (seção 7). Os totais vêm da mesma consulta de "Minhas
/// comissões" (<see cref="ConsultaLinhasComissao"/>); fechar e reabrir tomam a trava de quinzenas
/// do negócio (<see cref="TravaQuinzenas"/>), a mesma de concluir/reabrir/cancelar atendimento.
/// </summary>
public sealed class ServicoQuinzenas : IServicoQuinzenas
{
    private const string Entidade = nameof(PeriodoComissao);
    public const int TamanhoMaximoMotivo = 500;

    /// <summary>Status de quem ainda "deve" conclusão.</summary>
    private static readonly StatusAgendamento[] StatusSemConclusao = [StatusAgendamento.Agendado, StatusAgendamento.EmAtendimento];

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IRegistroAuditoria _auditoria;
    private readonly TravaQuinzenas _trava;

    public ServicoQuinzenas(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IUsuarioAtual usuarioAtual,
        IRegistroAuditoria auditoria, TravaQuinzenas trava)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _usuarioAtual = usuarioAtual;
        _auditoria = auditoria;
        _trava = trava;
    }

    public async Task<IReadOnlyList<QuinzenaResumo>> ListarAsync(CancellationToken cancellationToken = default)
    {
        var periodos = await _dbContext.PeriodosComissao.AsNoTracking().OrderBy(p => p.Inicio).ToListAsync(cancellationToken);
        var nomes = await NomesDosUsuariosAsync(periodos.Select(p => p.FechadoPorUsuarioId), cancellationToken);

        var resumos = new List<QuinzenaResumo>(periodos.Count);
        DateOnly? fimAnterior = null;
        foreach (var periodo in periodos)
        {
            resumos.Add(Resumir(periodo, nomes, fimAnterior));
            fimAnterior = periodo.Fim;
        }

        resumos.Reverse(); // mais recente primeiro
        return resumos;
    }

    public async Task<SugestaoQuinzena> SugerirProximaAsync(CancellationToken cancellationToken = default)
    {
        var fimDoUltimo = await _dbContext.PeriodosComissao.MaxAsync(p => (DateOnly?)p.Fim, cancellationToken);
        var fuso = await _trava.FusoAsync(cancellationToken);
        var hoje = ConversorFusoHorario.ParaLocal(DateTimeOffset.UtcNow, fuso).Dia;

        var (inicio, fim) = PeriodoComissao.SugerirProximo(fimDoUltimo, hoje);
        return new SugestaoQuinzena(inicio, fim);
    }

    public async Task<ResultadoQuinzena> CriarAsync(DateOnly inicio, DateOnly fim, CancellationToken cancellationToken = default)
    {
        if (inicio > fim)
            return ResultadoQuinzena.Falha(ErroQuinzena.DatasInvalidas, "O início não pode ser depois do fim.");

        var periodo = PeriodoComissao.Criar(_contextoNegocio.NegocioId!.Value, inicio, fim);
        _dbContext.PeriodosComissao.Add(periodo);
        _auditoria.Registrar(AcoesAuditoria.CriarQuinzena, Entidade, periodo.Id, Intervalo(inicio, fim));

        return await SalvarPeriodoAsync(periodo, cancellationToken);
    }

    public async Task<ResultadoQuinzena> AlterarAsync(Guid periodoId, DateOnly inicio, DateOnly fim, CancellationToken cancellationToken = default)
    {
        var periodo = await _dbContext.PeriodosComissao.FirstOrDefaultAsync(p => p.Id == periodoId, cancellationToken);
        if (periodo is null)
            return NaoEncontrada();
        if (periodo.Estado == EstadoPeriodoComissao.Fechada)
            return ResultadoQuinzena.Falha(ErroQuinzena.JaFechada, "Uma quinzena fechada não pode ter as datas alteradas. Reabra antes.");
        if (inicio > fim)
            return ResultadoQuinzena.Falha(ErroQuinzena.DatasInvalidas, "O início não pode ser depois do fim.");

        var antes = Intervalo(periodo.Inicio, periodo.Fim);
        periodo.AlterarDatas(inicio, fim);
        _auditoria.Registrar(AcoesAuditoria.EditarQuinzena, Entidade, periodo.Id, $"{antes} → {Intervalo(inicio, fim)}");

        return await SalvarPeriodoAsync(periodo, cancellationToken);
    }

    public async Task<ResultadoQuinzena> ExcluirAsync(Guid periodoId, CancellationToken cancellationToken = default)
    {
        var periodo = await _dbContext.PeriodosComissao.FirstOrDefaultAsync(p => p.Id == periodoId, cancellationToken);
        if (periodo is null)
            return NaoEncontrada();
        if (periodo.Estado == EstadoPeriodoComissao.Fechada)
            return ResultadoQuinzena.Falha(ErroQuinzena.JaFechada, "Uma quinzena fechada não pode ser excluída. Reabra antes.");

        _dbContext.PeriodosComissao.Remove(periodo);
        _auditoria.Registrar(AcoesAuditoria.ExcluirQuinzena, Entidade, periodo.Id, Intervalo(periodo.Inicio, periodo.Fim));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ResultadoQuinzena.Ok(periodo.Id);
    }

    public async Task<DetalheQuinzena?> DetalharAsync(Guid periodoId, CancellationToken cancellationToken = default)
    {
        var periodo = await _dbContext.PeriodosComissao.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodoId, cancellationToken);
        if (periodo is null)
            return null;

        var nomes = await NomesDosUsuariosAsync([periodo.FechadoPorUsuarioId], cancellationToken);
        var fimAnterior = await _dbContext.PeriodosComissao
            .Where(p => p.Fim < periodo.Inicio)
            .MaxAsync(p => (DateOnly?)p.Fim, cancellationToken);
        var resumo = Resumir(periodo, nomes, fimAnterior);

        if (periodo.Estado == EstadoPeriodoComissao.Fechada)
        {
            var linhasFechadas = await (
                from f in _dbContext.FechamentosComissao
                join p in _dbContext.Profissionais on f.ProfissionalId equals p.Id
                where f.PeriodoComissaoId == periodo.Id
                orderby p.Nome
                select new LinhaQuinzena(
                    p.Id, p.Nome, new TotaisComissao(f.TotalComissao, f.TotalCobrado, f.QuantidadeServicos),
                    f.TotalComissaoProdutos, f.TotalVales, f.TotalConsumo, f.Liquido, f.SaldoRestante))
                .ToListAsync(cancellationToken);
            return new DetalheQuinzena(resumo, false, linhasFechadas, []);
        }

        var calculo = await CalcularAsync(periodo, cancellationToken);
        return new DetalheQuinzena(resumo, true, calculo.Linhas, calculo.Pendentes);
    }

    public Task<ResultadoQuinzena> FecharAsync(Guid periodoId, bool confirmarPendentes, CancellationToken cancellationToken = default) =>
        NaTravaAsync(async () =>
        {
            var periodo = await _dbContext.PeriodosComissao.FirstOrDefaultAsync(p => p.Id == periodoId, cancellationToken);
            if (periodo is null)
                return NaoEncontrada();
            if (periodo.Estado == EstadoPeriodoComissao.Fechada)
                return ResultadoQuinzena.Falha(ErroQuinzena.JaFechada, "Esta quinzena já está fechada.");

            var calculo = await CalcularAsync(periodo, cancellationToken);
            if (calculo.Pendentes.Count > 0 && !confirmarPendentes)
                return ResultadoQuinzena.Falha(ErroQuinzena.PendentesSemConfirmacao,
                    $"Há {calculo.Pendentes.Count} atendimento(s) desta quinzena sem conclusão. Confirme para fechar mesmo assim.",
                    calculo.Pendentes);

            var agora = DateTimeOffset.UtcNow;
            var usuarioId = _usuarioAtual.UsuarioId;
            foreach (var linha in calculo.Linhas)
            {
                var fechamento = FechamentoComissao.Criar(
                    periodo.NegocioId, periodo.Id, linha.ProfissionalId, linha.Totais.TotalAtendido, linha.Totais.TotalComissao,
                    linha.Totais.QuantidadeServicos, usuarioId, agora);
                fechamento.RegistrarProdutosEDescontos(linha.ComissaoProdutos, linha.Vales, linha.Consumo, linha.SaldoRestante);
                _dbContext.FechamentosComissao.Add(fechamento);

                // Quitação do saldo devedor (seção 7): cada desconto fica ligado a este fechamento; reabrir apaga (volta a ficar em aberto).
                foreach (var desconto in calculo.Descontos.GetValueOrDefault(linha.ProfissionalId, []))
                    _dbContext.QuitacoesSaldo.Add(QuitacaoSaldo.Criar(periodo.NegocioId, fechamento.Id, desconto.LancamentoId, desconto.Valor));
            }

            periodo.Fechar(usuarioId, agora);
            _auditoria.Registrar(AcoesAuditoria.FecharQuinzena, Entidade, periodo.Id,
                $"{Intervalo(periodo.Inicio, periodo.Fim)}: {calculo.Linhas.Count} profissional(is), comissão total "
                + FormatacaoBrasil.Reais(calculo.Linhas.Sum(l => l.Totais.TotalComissao + l.ComissaoProdutos))
                + $", vale/consumo descontado {FormatacaoBrasil.Reais(calculo.Linhas.Sum(l => l.Vales + l.Consumo))}"
                + (calculo.Pendentes.Count > 0 ? $"; fechada com {calculo.Pendentes.Count} atendimento(s) sem conclusão" : ""));

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ResultadoQuinzena.Falha(ErroQuinzena.JaFechada, "Esta quinzena acabou de ser alterada por outra pessoa. Atualize a tela.");
            }

            return ResultadoQuinzena.Ok(periodo.Id);
        }, cancellationToken);

    public Task<ResultadoQuinzena> ReabrirAsync(Guid periodoId, string? motivo, CancellationToken cancellationToken = default)
    {
        var motivoLimpo = motivo?.Trim();
        if (string.IsNullOrEmpty(motivoLimpo))
            return Task.FromResult(ResultadoQuinzena.Falha(ErroQuinzena.MotivoObrigatorio, "Informe o motivo para reabrir a quinzena."));
        if (motivoLimpo.Length > TamanhoMaximoMotivo)
            motivoLimpo = motivoLimpo[..TamanhoMaximoMotivo];

        return NaTravaAsync(async () =>
        {
            var periodo = await _dbContext.PeriodosComissao.FirstOrDefaultAsync(p => p.Id == periodoId, cancellationToken);
            if (periodo is null)
                return NaoEncontrada();
            if (periodo.Estado != EstadoPeriodoComissao.Fechada)
                return ResultadoQuinzena.Falha(ErroQuinzena.NaoFechada, "Esta quinzena não está fechada.");

            var fechamentos = await _dbContext.FechamentosComissao.Where(f => f.PeriodoComissaoId == periodo.Id).ToListAsync(cancellationToken);
            _dbContext.FechamentosComissao.RemoveRange(fechamentos);
            periodo.Reabrir();
            _auditoria.Registrar(AcoesAuditoria.ReabrirQuinzena, Entidade, periodo.Id,
                $"{Intervalo(periodo.Inicio, periodo.Fim)}: fechamento de {FormatacaoBrasil.Reais(fechamentos.Sum(f => f.TotalComissao))} "
                + $"apagado ({fechamentos.Count} profissional(is)). Motivo: {motivoLimpo}");

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ResultadoQuinzena.Falha(ErroQuinzena.NaoFechada, "Esta quinzena acabou de ser alterada por outra pessoa. Atualize a tela.");
            }

            return ResultadoQuinzena.Ok(periodo.Id);
        }, cancellationToken);
    }

    public async Task<QuinzenasDoProfissional> ListarMinhasAsync(CancellationToken cancellationToken = default)
    {
        var usuarioId = _usuarioAtual.UsuarioId ?? throw new InvalidOperationException("Sem usuário logado.");
        var profissional = await (
            from u in _dbContext.Usuarios
            join p in _dbContext.Profissionais on u.ProfissionalId equals p.Id
            where u.Id == usuarioId
            select new { p.Id, p.AcertoPorQuinzena }).FirstOrDefaultAsync(cancellationToken);

        if (profissional is null || !profissional.AcertoPorQuinzena)
            return new QuinzenasDoProfissional(false, []);

        var periodos = await _dbContext.PeriodosComissao.AsNoTracking().OrderByDescending(p => p.Inicio).ToListAsync(cancellationToken);
        var meusFechamentos = await _dbContext.FechamentosComissao.AsNoTracking()
            .Where(f => f.ProfissionalId == profissional.Id)
            .ToDictionaryAsync(f => f.PeriodoComissaoId, cancellationToken);
        var fuso = await _trava.FusoAsync(cancellationToken);

        var quinzenas = new List<QuinzenaDoProfissional>();
        foreach (var periodo in periodos)
        {
            if (periodo.Estado == EstadoPeriodoComissao.Fechada)
            {
                // Fechada sem fechamento meu: eu não estava no acerto quando fechou.
                if (meusFechamentos.TryGetValue(periodo.Id, out var f))
                    quinzenas.Add(new QuinzenaDoProfissional(periodo.Id, periodo.Inicio, periodo.Fim, periodo.Estado.ToString(), false,
                        new TotaisComissao(f.TotalComissao, f.TotalCobrado, f.QuantidadeServicos),
                        f.TotalComissaoProdutos, f.TotalVales, f.TotalConsumo, f.Liquido, f.SaldoRestante));
                continue;
            }

            var linha = (await CalcularLinhasAsync(periodo, fuso, [(profissional.Id, string.Empty)], cancellationToken)).Linhas.Single();
            quinzenas.Add(new QuinzenaDoProfissional(periodo.Id, periodo.Inicio, periodo.Fim, periodo.Estado.ToString(), true,
                linha.Totais, linha.ComissaoProdutos, linha.Vales, linha.Consumo, linha.Liquido, linha.SaldoRestante));
        }

        return new QuinzenasDoProfissional(true, quinzenas);
    }

    // ---------------------------------------------------------------- apoio

    private sealed record Calculo(
        IReadOnlyList<LinhaQuinzena> Linhas, IReadOnlyList<AtendimentoPendente> Pendentes,
        IReadOnlyDictionary<Guid, IReadOnlyList<DescontoAplicado>> Descontos);

    /// <summary>Totais ao vivo de quem tem acerto por quinzena e os atendimentos do período ainda sem conclusão.</summary>
    private async Task<Calculo> CalcularAsync(PeriodoComissao periodo, CancellationToken cancellationToken)
    {
        var profissionais = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.AcertoPorQuinzena && !p.Excluido)
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome })
            .ToListAsync(cancellationToken);
        var ids = profissionais.Select(p => p.Id).ToList();

        var fuso = await _trava.FusoAsync(cancellationToken);
        var (linhas, descontos) = await CalcularLinhasAsync(periodo, fuso, profissionais.Select(p => (p.Id, p.Nome)).ToList(), cancellationToken);

        var (inicioUtc, fimUtc) = ConsultaLinhasComissao.IntervaloUtc(periodo.Inicio, periodo.Fim, fuso);
        var nomes = profissionais.ToDictionary(p => p.Id, p => p.Nome);
        var pendentes = (await _dbContext.Agendamentos.AsNoTracking()
                .Where(a => ids.Contains(a.ProfissionalId) && StatusSemConclusao.Contains(a.Status)
                    && a.Inicio >= inicioUtc && a.Inicio < fimUtc)
                .OrderBy(a => a.Inicio)
                .Select(a => new { a.Id, a.Inicio, a.ProfissionalId, a.Status })
                .ToListAsync(cancellationToken))
            .Select(a => new AtendimentoPendente(a.Id, a.Inicio, nomes[a.ProfissionalId], a.Status.ToString()))
            .ToList();

        return new Calculo(linhas, pendentes, descontos);
    }

    /// <summary>
    /// Comissão de serviço e de produto do período, e a quitação do saldo devedor: vales e consumos em aberto até o fim
    /// da quinzena, do mais antigo para o mais novo, descontados da comissão bruta até ela acabar (seção 7).
    /// </summary>
    private async Task<(IReadOnlyList<LinhaQuinzena> Linhas, IReadOnlyDictionary<Guid, IReadOnlyList<DescontoAplicado>> Descontos)> CalcularLinhasAsync(
        PeriodoComissao periodo, TimeZoneInfo fuso, IReadOnlyList<(Guid Id, string Nome)> profissionais, CancellationToken cancellationToken)
    {
        var ids = profissionais.Select(p => p.Id).ToList();
        var totais = await TotaisPorProfissionalAsync(periodo, fuso, ids, cancellationToken);

        var (inicioUtc, fimUtc) = ConsultaLinhasComissao.IntervaloUtc(periodo.Inicio, periodo.Fim, fuso);
        var produtos = await _dbContext.VendasProduto.AsNoTracking()
            .Where(v => v.VendedorProfissionalId != null && ids.Contains(v.VendedorProfissionalId.Value) && v.EstornadaEm == null
                && v.Data >= inicioUtc && v.Data < fimUtc)
            .GroupBy(v => v.VendedorProfissionalId!.Value)
            .Select(g => new { ProfissionalId = g.Key, Comissao = g.Sum(v => v.ValorComissao) })
            .ToDictionaryAsync(g => g.ProfissionalId, g => g.Comissao, cancellationToken);

        var emAberto = (await ConsultaSaldoDevedor.EmAberto(_dbContext)
                .Where(l => ids.Contains(l.ProfissionalId) && l.Data <= periodo.Fim)
                .DoMaisAntigo()
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.ProfissionalId);

        var linhas = new List<LinhaQuinzena>(profissionais.Count);
        var descontos = new Dictionary<Guid, IReadOnlyList<DescontoAplicado>>();
        foreach (var (id, nome) in profissionais)
        {
            var servico = totais.GetValueOrDefault(id, TotaisComissao.Zero);
            var produto = produtos.GetValueOrDefault(id);
            var (aplicados, liquido, restante) = CalculadoraQuitacao.Aplicar(
                servico.TotalComissao + produto, emAberto[id].Select(l => new SaldoEmAberto(l.Id, l.Tipo, l.Aberto)));

            descontos[id] = aplicados;
            linhas.Add(new LinhaQuinzena(
                id, nome, servico, produto,
                aplicados.Where(d => d.Tipo == TipoLancamentoSaldo.Vale).Sum(d => d.Valor),
                aplicados.Where(d => d.Tipo == TipoLancamentoSaldo.ConsumoInterno).Sum(d => d.Valor),
                liquido, restante));
        }

        return (linhas, descontos);
    }

    private async Task<Dictionary<Guid, TotaisComissao>> TotaisPorProfissionalAsync(
        PeriodoComissao periodo, TimeZoneInfo fuso, IReadOnlyCollection<Guid> profissionais, CancellationToken cancellationToken)
    {
        var (inicioUtc, fimUtc) = ConsultaLinhasComissao.IntervaloUtc(periodo.Inicio, periodo.Fim, fuso);
        return await ConsultaLinhasComissao.Concluidas(_dbContext, inicioUtc, fimUtc)
            .Where(l => profissionais.Contains(l.ProfissionalId))
            .GroupBy(l => l.ProfissionalId)
            .Select(g => new { ProfissionalId = g.Key, Comissao = g.Sum(l => l.Comissao), Base = g.Sum(l => l.ValorBase), Quantidade = g.Count() })
            .ToDictionaryAsync(g => g.ProfissionalId, g => new TotaisComissao(g.Comissao, g.Base, g.Quantidade), cancellationToken);
    }

    /// <summary>Grava criação/edição. Sobreposição é recusada pelo banco (exclusion constraint), inclusive entre requisições simultâneas.</summary>
    private async Task<ResultadoQuinzena> SalvarPeriodoAsync(PeriodoComissao periodo, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeExclusao())
        {
            _dbContext.ChangeTracker.Clear();
            return ResultadoQuinzena.Falha(ErroQuinzena.Sobreposicao, "Essas datas se sobrepõem a outra quinzena. Escolha datas que não se cruzem.");
        }

        return ResultadoQuinzena.Ok(periodo.Id, await AvisoDeBuracoAsync(periodo, cancellationToken));
    }

    /// <summary>Buracos entre quinzenas são permitidos (seção 7), só avisados.</summary>
    private async Task<string?> AvisoDeBuracoAsync(PeriodoComissao periodo, CancellationToken cancellationToken)
    {
        var fimAnterior = await _dbContext.PeriodosComissao.Where(p => p.Fim < periodo.Inicio).MaxAsync(p => (DateOnly?)p.Fim, cancellationToken);
        var inicioSeguinte = await _dbContext.PeriodosComissao.Where(p => p.Inicio > periodo.Fim).MinAsync(p => (DateOnly?)p.Inicio, cancellationToken);

        var avisos = new List<string>();
        if (fimAnterior is { } fa && fa.AddDays(1) < periodo.Inicio)
            avisos.Add($"de {Data(fa.AddDays(1))} a {Data(periodo.Inicio.AddDays(-1))}");
        if (inicioSeguinte is { } inicio && periodo.Fim.AddDays(1) < inicio)
            avisos.Add($"de {Data(periodo.Fim.AddDays(1))} a {Data(inicio.AddDays(-1))}");

        return avisos.Count == 0 ? null : $"Ficaram dias sem quinzena ({string.Join(" e ", avisos)}): atendimentos nesses dias não entram em nenhum fechamento.";
    }

    private Task<ResultadoQuinzena> NaTravaAsync(Func<Task<ResultadoQuinzena>> acao, CancellationToken cancellationToken)
    {
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        return estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await _trava.TravarNegocioAsync(cancellationToken);
            var resultado = await acao();
            if (resultado.Sucesso)
                await transacao.CommitAsync(cancellationToken);
            return resultado;
        });
    }

    private async Task<Dictionary<Guid, string>> NomesDosUsuariosAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var lista = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        return lista.Count == 0
            ? []
            : await _dbContext.Usuarios.AsNoTracking().Where(u => lista.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, cancellationToken);
    }

    private static QuinzenaResumo Resumir(PeriodoComissao periodo, Dictionary<Guid, string> nomes, DateOnly? fimAnterior) => new(
        periodo.Id, periodo.Inicio, periodo.Fim, periodo.Estado.ToString(), periodo.FechadoEm,
        periodo.FechadoPorUsuarioId is { } u && nomes.TryGetValue(u, out var nome) ? nome : null,
        fimAnterior is { } fa ? Math.Max(0, periodo.Inicio.DayNumber - fa.DayNumber - 1) : 0);

    private static ResultadoQuinzena NaoEncontrada() => ResultadoQuinzena.Falha(ErroQuinzena.NaoEncontrada, "Quinzena não encontrada.");

    private static string Data(DateOnly data) => data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Intervalo(DateOnly inicio, DateOnly fim) => $"{Data(inicio)} a {Data(fim)}";
}
