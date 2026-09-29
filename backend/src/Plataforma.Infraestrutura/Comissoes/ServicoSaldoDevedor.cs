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
    public Guid? ProfissionalId { get; init; }
    public Guid? UsuarioId { get; init; }
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
            UsuarioId = l.UsuarioId,
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
        var pessoa = PessoaComissao.Criar(dados.ProfissionalId, dados.UsuarioId);
        var nome = await NomeDaPessoaAtivaAsync(pessoa, cancellationToken);
        var data = dados.Data ?? await HojeAsync(cancellationToken);

        var vale = LancamentoSaldoDevedor.CriarVale(
            _contextoNegocio.NegocioId!.Value, pessoa, dados.Valor, data, dados.Motivo, _usuarioAtual.UsuarioId);
        _dbContext.LancamentosSaldoDevedor.Add(vale);
        _auditoria.Registrar(AcoesAuditoria.LancarVale, nameof(LancamentoSaldoDevedor), vale.Id,
            $"{nome}: {FormatacaoBrasil.Reais(vale.Valor)} em {vale.Data:dd/MM/yyyy}" + (vale.Descricao is null ? "" : $". Motivo: {vale.Descricao}"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return vale.Id;
    }

    public async Task<Guid?> LancarConsumoAsync(Guid produtoId, LancarConsumo dados, CancellationToken cancellationToken = default)
    {
        var pessoa = PessoaComissao.Criar(dados.ProfissionalId, dados.UsuarioId);
        var nome = await NomeDaPessoaAtivaAsync(pessoa, cancellationToken);
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
                pessoa, produto, dados.Quantidade, dados.ValorUnitario ?? produto.PrecoCusto, hoje, dados.Observacao,
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
            .GroupBy(l => new { l.ProfissionalId, l.UsuarioId, l.Tipo })
            .Select(g => new { g.Key.ProfissionalId, g.Key.UsuarioId, g.Key.Tipo, Aberto = g.Sum(l => l.Aberto) })
            .ToListAsync(cancellationToken);

        var comSaldo = abertos.Where(a => a.ProfissionalId != null).Select(a => a.ProfissionalId!.Value).Distinct().ToList();
        var profissionais = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => (p.Ativo && !p.Excluido) || comSaldo.Contains(p.Id))
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome, p.Ativo })
            .ToListAsync(cancellationToken);

        // Usuários sem cadastro de profissional: quem tem acerto por quinzena (é quem recebe por ela) ou já teve lançamento.
        var comLancamento = await _dbContext.LancamentosSaldoDevedor
            .Where(l => l.UsuarioId != null)
            .Select(l => l.UsuarioId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var usuarios = await _dbContext.Usuarios.AsNoTracking()
            .Where(u => (u.AcertoPorQuinzena && u.ProfissionalId == null && u.Ativo && !u.Excluido) || comLancamento.Contains(u.Id))
            .OrderBy(u => u.Nome)
            .Select(u => new { u.Id, u.Nome, u.Ativo })
            .ToListAsync(cancellationToken);

        decimal Soma(PessoaComissao pessoa, TipoLancamentoSaldo tipo) =>
            abertos.Where(a => PessoaComissao.Criar(a.ProfissionalId, a.UsuarioId) == pessoa && a.Tipo == tipo).Sum(a => a.Aberto);

        SaldoDoProfissional Linha(PessoaComissao pessoa, string nome, bool ativo) => new(
            pessoa.ProfissionalId, nome, ativo, Soma(pessoa, TipoLancamentoSaldo.Vale), Soma(pessoa, TipoLancamentoSaldo.ConsumoInterno),
            pessoa.UsuarioId);

        return profissionais.Select(p => Linha(PessoaComissao.Profissional(p.Id), p.Nome, p.Ativo))
            .Concat(usuarios.Select(u => Linha(PessoaComissao.Usuario(u.Id), u.Nome, u.Ativo)))
            .ToList();
    }

    public async Task<SaldoDevedor?> DetalharAsync(Guid profissionalId, CancellationToken cancellationToken = default)
    {
        var nome = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Id == profissionalId)
            .Select(p => p.Nome)
            .FirstOrDefaultAsync(cancellationToken);
        return nome is null ? null : await DetalharPessoaAsync(PessoaComissao.Profissional(profissionalId), nome, cancellationToken);
    }

    public async Task<SaldoDevedor?> DetalharUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var nome = await _dbContext.Usuarios.AsNoTracking()
            .Where(u => u.Id == usuarioId && u.ProfissionalId == null)
            .Select(u => u.Nome)
            .FirstOrDefaultAsync(cancellationToken);
        return nome is null ? null : await DetalharPessoaAsync(PessoaComissao.Usuario(usuarioId), nome, cancellationToken);
    }

    private async Task<SaldoDevedor> DetalharPessoaAsync(PessoaComissao pessoa, string nomePessoa, CancellationToken cancellationToken)
    {
        var (profissionalId, usuarioId) = (pessoa.ProfissionalId, pessoa.UsuarioId);
        var abertos = await ConsultaSaldoDevedor.Todos(_dbContext)
            .Where(l => profissionalId != null ? l.ProfissionalId == profissionalId : l.UsuarioId == usuarioId)
            .ToDictionaryAsync(l => l.Id, l => l.Aberto, cancellationToken);

        var lancamentos = await _dbContext.LancamentosSaldoDevedor.AsNoTracking()
            .Where(l => profissionalId != null ? l.ProfissionalId == profissionalId : l.UsuarioId == usuarioId)
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
            profissionalId, nomePessoa,
            itens.Where(i => i.Tipo == nameof(TipoLancamentoSaldo.Vale)).Sum(i => i.Aberto),
            itens.Where(i => i.Tipo == nameof(TipoLancamentoSaldo.ConsumoInterno)).Sum(i => i.Aberto),
            itens, usuarioId);
    }

    public async Task<SaldoDevedor?> DetalharMeuAsync(CancellationToken cancellationToken = default)
    {
        var usuarioId = _usuarioAtual.UsuarioId ?? throw new InvalidOperationException("Sem usuário logado.");
        var usuario = await _dbContext.Usuarios.AsNoTracking()
            .Where(u => u.Id == usuarioId)
            .Select(u => new { u.ProfissionalId, u.AcertoPorQuinzena })
            .FirstOrDefaultAsync(cancellationToken);
        if (usuario is null)
            return null;
        if (usuario.ProfissionalId is { } id)
            return await DetalharAsync(id, cancellationToken);

        // Sem vínculo: só quem recebe por quinzena ou já teve vale/consumo tem o que ver.
        var temLancamento = await _dbContext.LancamentosSaldoDevedor.AnyAsync(l => l.UsuarioId == usuarioId, cancellationToken);
        return usuario.AcertoPorQuinzena || temLancamento ? await DetalharUsuarioAsync(usuarioId, cancellationToken) : null;
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

    /// <summary>Usuário vinculado a profissional é recusado: o saldo dele fica no profissional.</summary>
    private async Task<string> NomeDaPessoaAtivaAsync(PessoaComissao pessoa, CancellationToken cancellationToken) =>
        pessoa.ProfissionalId is { } profissionalId
            ? await _dbContext.Profissionais.AsNoTracking()
                  .Where(p => p.Id == profissionalId && p.Ativo && !p.Excluido)
                  .Select(p => p.Nome)
                  .FirstOrDefaultAsync(cancellationToken)
              ?? throw new ArgumentException("Escolha um profissional ativo.")
            : await _dbContext.Usuarios.AsNoTracking()
                  .Where(u => u.Id == pessoa.UsuarioId && u.Ativo && !u.Excluido && u.ProfissionalId == null)
                  .Select(u => u.Nome)
                  .FirstOrDefaultAsync(cancellationToken)
              ?? throw new ArgumentException("Escolha um usuário ativo sem cadastro de profissional.");

    private async Task<DateOnly> HojeAsync(CancellationToken cancellationToken) =>
        ConversorFusoHorario.ParaLocal(DateTimeOffset.UtcNow, await _trava.FusoAsync(cancellationToken)).Dia;

    private static string Rotulo(TipoLancamentoSaldo tipo) => tipo == TipoLancamentoSaldo.Vale ? "Vale" : "Consumo interno";

    private static string Estorno(decimal estornado) =>
        estornado > 0 ? $"; {FormatacaoBrasil.Reais(estornado)} já descontado em fechamento foi devolvido ao líquido dele" : "";
}
