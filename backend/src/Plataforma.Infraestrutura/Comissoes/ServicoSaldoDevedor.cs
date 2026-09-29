using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Dominio.Comissoes;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.Estoque;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Comissoes;

/// <summary>Um lançamento com o quanto ainda está em aberto. Classe com init: o EF só traduz filtro sobre projeção por inicializador.</summary>
internal sealed class LinhaSaldo
{
    public Guid Id { get; init; }
    public Guid ProfissionalId { get; init; }
    public TipoLancamentoSaldo Tipo { get; init; }
    public DateOnly Data { get; init; }
    public DateTimeOffset CriadoEm { get; init; }
    public decimal Valor { get; init; }
    public decimal Aberto { get; init; }
}

/// <summary>"Em aberto" = valor do lançamento menos o que já foi descontado em fechamentos (a quitação pode ser parcial).</summary>
internal static class ConsultaSaldoDevedor
{
    public static IQueryable<LinhaSaldo> Todos(PlataformaDbContext dbContext) =>
        from l in dbContext.LancamentosSaldoDevedor
        select new LinhaSaldo
        {
            Id = l.Id,
            ProfissionalId = l.ProfissionalId,
            Tipo = l.Tipo,
            Data = l.Data,
            CriadoEm = l.CriadoEm,
            Valor = l.Valor,
            Aberto = l.Valor - (dbContext.QuitacoesSaldo.Where(q => q.LancamentoId == l.Id).Sum(q => (decimal?)q.Valor) ?? 0m),
        };

    public static IQueryable<LinhaSaldo> EmAberto(PlataformaDbContext dbContext) => Todos(dbContext).Where(l => l.Aberto > 0);

    /// <summary>Ordem da quitação (seção 7): do mais antigo para o mais novo.</summary>
    public static IOrderedQueryable<LinhaSaldo> DoMaisAntigo(this IQueryable<LinhaSaldo> linhas) =>
        linhas.OrderBy(l => l.Data).ThenBy(l => l.CriadoEm).ThenBy(l => l.Id);
}

public sealed class ServicoSaldoDevedor : IServicoSaldoDevedor
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IRegistroAuditoria _auditoria;
    private readonly TravaQuinzenas _trava;

    public ServicoSaldoDevedor(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IUsuarioAtual usuarioAtual, IRegistroAuditoria auditoria,
        TravaQuinzenas trava)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _usuarioAtual = usuarioAtual;
        _auditoria = auditoria;
        _trava = trava;
    }

    public async Task<Guid> LancarValeAsync(LancarVale dados, CancellationToken cancellationToken = default)
    {
        var nome = await NomeDoProfissionalAtivoAsync(dados.ProfissionalId, cancellationToken);
        var data = dados.Data ?? await HojeAsync(cancellationToken);

        var vale = LancamentoSaldoDevedor.CriarVale(
            _contextoNegocio.NegocioId!.Value, dados.ProfissionalId, dados.Valor, data, dados.Motivo, _usuarioAtual.UsuarioId);
        _dbContext.LancamentosSaldoDevedor.Add(vale);
        _auditoria.Registrar(AcoesAuditoria.LancarVale, nameof(LancamentoSaldoDevedor), vale.Id,
            $"{nome}: {FormatacaoBrasil.Reais(vale.Valor)} em {vale.Data:dd/MM/yyyy}" + (vale.Descricao is null ? "" : $". Motivo: {vale.Descricao}"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return vale.Id;
    }

    public async Task<Guid?> LancarConsumoAsync(Guid produtoId, LancarConsumo dados, CancellationToken cancellationToken = default)
    {
        var nome = await NomeDoProfissionalAtivoAsync(dados.ProfissionalId, cancellationToken);
        var hoje = await HojeAsync(cancellationToken);
        var negocioId = _contextoNegocio.NegocioId!.Value;

        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var produto = await TravaProduto.TravarAsync(_dbContext, negocioId, produtoId, cancellationToken);
            if (produto is null)
                return (Guid?)null;

            var (consumo, movimento) = LancamentoSaldoDevedor.CriarConsumo(
                dados.ProfissionalId, produto, dados.Quantidade, dados.ValorUnitario ?? produto.PrecoCusto, hoje, dados.Observacao,
                _usuarioAtual.UsuarioId, DateTimeOffset.UtcNow);
            _dbContext.MovimentosEstoque.Add(movimento);
            _dbContext.LancamentosSaldoDevedor.Add(consumo);
            _auditoria.Registrar(AcoesAuditoria.LancarConsumoInterno, nameof(LancamentoSaldoDevedor), consumo.Id,
                $"{nome}: {produto.Nome} × {dados.Quantidade} ({movimento.QuantidadeAntes} → {movimento.QuantidadeDepois}), "
                + FormatacaoBrasil.Reais(consumo.Valor));

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return consumo.Id;
        });
    }

    public async Task<IReadOnlyList<SaldoDoProfissional>> ResumirAsync(CancellationToken cancellationToken = default)
    {
        var abertos = await ConsultaSaldoDevedor.EmAberto(_dbContext)
            .GroupBy(l => new { l.ProfissionalId, l.Tipo })
            .Select(g => new { g.Key.ProfissionalId, g.Key.Tipo, Aberto = g.Sum(l => l.Aberto) })
            .ToListAsync(cancellationToken);

        var comSaldo = abertos.Select(a => a.ProfissionalId).Distinct().ToList();
        var profissionais = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => (p.Ativo && !p.Excluido) || comSaldo.Contains(p.Id))
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome, p.Ativo })
            .ToListAsync(cancellationToken);

        decimal Soma(Guid id, TipoLancamentoSaldo tipo) =>
            abertos.Where(a => a.ProfissionalId == id && a.Tipo == tipo).Sum(a => a.Aberto);

        return profissionais
            .Select(p => new SaldoDoProfissional(p.Id, p.Nome, p.Ativo, Soma(p.Id, TipoLancamentoSaldo.Vale), Soma(p.Id, TipoLancamentoSaldo.ConsumoInterno)))
            .ToList();
    }

    public async Task<SaldoDevedor?> DetalharAsync(Guid profissionalId, CancellationToken cancellationToken = default)
    {
        var profissional = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Id == profissionalId)
            .Select(p => new { p.Id, p.Nome })
            .FirstOrDefaultAsync(cancellationToken);
        if (profissional is null)
            return null;

        var abertos = await ConsultaSaldoDevedor.Todos(_dbContext)
            .Where(l => l.ProfissionalId == profissionalId)
            .ToDictionaryAsync(l => l.Id, l => l.Aberto, cancellationToken);

        var lancamentos = await _dbContext.LancamentosSaldoDevedor.AsNoTracking()
            .Where(l => l.ProfissionalId == profissionalId)
            .OrderByDescending(l => l.Data).ThenByDescending(l => l.CriadoEm)
            .ToListAsync(cancellationToken);

        var autores = lancamentos.Where(l => l.LancadoPorUsuarioId != null).Select(l => l.LancadoPorUsuarioId!.Value).Distinct().ToList();
        var nomes = await _dbContext.Usuarios.AsNoTracking()
            .Where(u => autores.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nome, cancellationToken);

        var itens = lancamentos
            .Select(l => new LancamentoSaldoResumo(
                l.Id, l.Tipo.ToString(), l.Data, l.Valor, abertos.GetValueOrDefault(l.Id), l.Descricao, l.NomeProduto, l.Quantidade,
                l.ValorUnitario, l.LancadoPorUsuarioId is { } u && nomes.TryGetValue(u, out var nome) ? nome : null))
            .ToList();

        return new SaldoDevedor(
            profissional.Id, profissional.Nome,
            itens.Where(i => i.Tipo == nameof(TipoLancamentoSaldo.Vale)).Sum(i => i.Aberto),
            itens.Where(i => i.Tipo == nameof(TipoLancamentoSaldo.ConsumoInterno)).Sum(i => i.Aberto),
            itens);
    }

    public async Task<SaldoDevedor?> DetalharMeuAsync(CancellationToken cancellationToken = default)
    {
        var usuarioId = _usuarioAtual.UsuarioId ?? throw new InvalidOperationException("Sem usuário logado.");
        var profissionalId = await _dbContext.Usuarios.Where(u => u.Id == usuarioId).Select(u => u.ProfissionalId).FirstOrDefaultAsync(cancellationToken);
        return profissionalId is { } id ? await DetalharAsync(id, cancellationToken) : null;
    }

    public Task<ResultadoLancamentoSaldo> AlterarAsync(
        Guid lancamentoId, AlterarLancamentoSaldo dados, Func<string, bool> podeMexer, CancellationToken cancellationToken = default) =>
        NaTravaAsync(lancamentoId, dados.ConfirmarQuitado, podeMexer, async (lancamento, estornado) =>
        {
            var antes = FormatacaoBrasil.Reais(lancamento.Valor);
            if (lancamento.Tipo == TipoLancamentoSaldo.Vale)
                lancamento.AlterarVale(dados.Valor ?? lancamento.Valor, dados.Data ?? lancamento.Data, dados.Descricao);
            else
                lancamento.AlterarConsumo(dados.ValorUnitario ?? lancamento.ValorUnitario ?? 0m, dados.Descricao);

            _auditoria.Registrar(AcoesAuditoria.EditarLancamentoSaldo, nameof(LancamentoSaldoDevedor), lancamento.Id,
                $"{Rotulo(lancamento.Tipo)}: {antes} → {FormatacaoBrasil.Reais(lancamento.Valor)}" + Estorno(estornado));
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<ResultadoLancamentoSaldo> ExcluirAsync(
        Guid lancamentoId, bool confirmarQuitado, Func<string, bool> podeMexer, CancellationToken cancellationToken = default) =>
        NaTravaAsync(lancamentoId, confirmarQuitado, podeMexer, async (lancamento, estornado) =>
        {
            var detalhe = $"{Rotulo(lancamento.Tipo)} de {FormatacaoBrasil.Reais(lancamento.Valor)} excluído";

            // Consumo excluído devolve o produto ao estoque, sempre por movimento.
            if (lancamento is { Tipo: TipoLancamentoSaldo.ConsumoInterno, ProdutoId: { } produtoId, Quantidade: { } quantidade })
            {
                var produto = await TravaProduto.TravarAsync(_dbContext, lancamento.NegocioId, produtoId, cancellationToken);
                if (produto is not null)
                {
                    var movimento = produto.RegistrarEntrada(
                        quantidade, lancamento.ValorUnitario ?? 0m, null, "Estorno de consumo interno", _usuarioAtual.UsuarioId, DateTimeOffset.UtcNow);
                    _dbContext.MovimentosEstoque.Add(movimento);
                    detalhe += $"; {produto.Nome} × {quantidade} volta ao estoque ({movimento.QuantidadeAntes} → {movimento.QuantidadeDepois})";
                }
            }

            _dbContext.LancamentosSaldoDevedor.Remove(lancamento);
            _auditoria.Registrar(AcoesAuditoria.ExcluirLancamentoSaldo, nameof(LancamentoSaldoDevedor), lancamento.Id, detalhe + Estorno(estornado));
        }, cancellationToken);

    /// <summary>
    /// Edição e exclusão tomam a trava de quinzenas (a mesma do fechamento). Lançamento já descontado num fechamento só
    /// com confirmação: as quitações dele saem e o líquido daquele fechamento sobe na mesma medida.
    /// </summary>
    private async Task<ResultadoLancamentoSaldo> NaTravaAsync(
        Guid lancamentoId, bool confirmarQuitado, Func<string, bool> podeMexer,
        Func<LancamentoSaldoDevedor, decimal, Task> acao, CancellationToken cancellationToken)
    {
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await _trava.TravarNegocioAsync(cancellationToken);

            var lancamento = await _dbContext.LancamentosSaldoDevedor.FirstOrDefaultAsync(l => l.Id == lancamentoId, cancellationToken);
            if (lancamento is null)
                return ResultadoLancamentoSaldo.Falha(ErroLancamentoSaldo.NaoEncontrado, "Lançamento não encontrado.");
            if (!podeMexer(lancamento.Tipo.ToString()))
                return ResultadoLancamentoSaldo.Falha(ErroLancamentoSaldo.SemPermissao, "Sem permissão para este tipo de lançamento.");

            var quitacoes = await _dbContext.QuitacoesSaldo.Where(q => q.LancamentoId == lancamentoId).ToListAsync(cancellationToken);
            if (quitacoes.Count > 0 && !confirmarQuitado)
                return ResultadoLancamentoSaldo.Falha(ErroLancamentoSaldo.QuitadoSemConfirmacao,
                    "Este lançamento já foi descontado num fechamento de quinzena. Mexer nele muda o valor daquele fechamento. Confirme para continuar.");

            var fechamentoIds = quitacoes.Select(q => q.FechamentoComissaoId).Distinct().ToList();
            var fechamentos = await _dbContext.FechamentosComissao.Where(f => fechamentoIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
            foreach (var quitacao in quitacoes)
                fechamentos[quitacao.FechamentoComissaoId].EstornarDesconto(lancamento.Tipo, quitacao.Valor);
            _dbContext.QuitacoesSaldo.RemoveRange(quitacoes);

            await acao(lancamento, quitacoes.Sum(q => q.Valor));
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return ResultadoLancamentoSaldo.Ok;
        });
    }

    private async Task<string> NomeDoProfissionalAtivoAsync(Guid profissionalId, CancellationToken cancellationToken) =>
        await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Id == profissionalId && p.Ativo && !p.Excluido)
            .Select(p => p.Nome)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new ArgumentException("Escolha um profissional ativo.");

    private async Task<DateOnly> HojeAsync(CancellationToken cancellationToken) =>
        ConversorFusoHorario.ParaLocal(DateTimeOffset.UtcNow, await _trava.FusoAsync(cancellationToken)).Dia;

    private static string Rotulo(TipoLancamentoSaldo tipo) => tipo == TipoLancamentoSaldo.Vale ? "Vale" : "Consumo interno";

    private static string Estorno(decimal estornado) =>
        estornado > 0 ? $"; {FormatacaoBrasil.Reais(estornado)} já descontado em fechamento foi devolvido ao líquido dele" : "";
}
