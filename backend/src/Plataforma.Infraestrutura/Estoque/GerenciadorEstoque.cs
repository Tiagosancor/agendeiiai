using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Cadastros;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Dominio.Estoque;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Estoque;

public sealed class GerenciadorEstoque : IGerenciadorEstoque
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IRegistroAuditoria _auditoria;

    public GerenciadorEstoque(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IUsuarioAtual usuarioAtual, IRegistroAuditoria auditoria)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _usuarioAtual = usuarioAtual;
        _auditoria = auditoria;
    }

    public async Task<IReadOnlyList<ProdutoResumo>> ListarAsync(CancellationToken cancellationToken = default) =>
        (await _dbContext.Produtos.AsNoTracking().OrderBy(p => p.Nome).ToListAsync(cancellationToken)).Select(Mapear).ToList();

    public async Task<Guid> CriarAsync(CriarProduto dados, CancellationToken cancellationToken = default)
    {
        if (dados.QuantidadeInicial < 0)
            throw new ArgumentException("A quantidade inicial não pode ser negativa.");

        var produto = Produto.Criar(
            _contextoNegocio.NegocioId!.Value, dados.Nome, dados.Categoria, dados.PrecoCusto, dados.PrecoVenda,
            dados.QuantidadeMinima ?? Produto.QuantidadeMinimaPadrao);
        _dbContext.Produtos.Add(produto);

        // O estoque de partida também é um movimento: a soma dos movimentos sempre bate com o número atual.
        if (dados.QuantidadeInicial > 0)
            _dbContext.MovimentosEstoque.Add(produto.RegistrarEntrada(
                dados.QuantidadeInicial, produto.PrecoCusto, null, "Estoque inicial", _usuarioAtual.UsuarioId, DateTimeOffset.UtcNow));

        _auditoria.Registrar(AcoesAuditoria.CriarProduto, nameof(Produto), produto.Id,
            $"Nome: {produto.Nome}; estoque inicial: {dados.QuantidadeInicial}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return produto.Id;
    }

    public async Task<bool> AtualizarAsync(Guid produtoId, AtualizarProduto dados, CancellationToken cancellationToken = default)
    {
        var produto = await _dbContext.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId, cancellationToken);
        if (produto is null)
            return false;

        var alterados = new List<string>();
        if (produto.Nome != dados.Nome.Trim()) alterados.Add("nome");
        if (produto.Categoria != (string.IsNullOrWhiteSpace(dados.Categoria) ? null : dados.Categoria.Trim())) alterados.Add("categoria");
        if (produto.PrecoCusto != decimal.Round(dados.PrecoCusto, 2)) alterados.Add("preço de custo");
        if (produto.PrecoVenda != decimal.Round(dados.PrecoVenda, 2)) alterados.Add("preço de venda");
        if (produto.QuantidadeMinima != dados.QuantidadeMinima) alterados.Add("quantidade mínima");

        produto.AtualizarDados(dados.Nome, dados.Categoria, dados.PrecoCusto, dados.PrecoVenda, dados.QuantidadeMinima);
        if (alterados.Count > 0)
            _auditoria.Registrar(AcoesAuditoria.Editar, nameof(Produto), produto.Id, $"Campos: {string.Join(", ", alterados)}");

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AlterarAtivoAsync(Guid produtoId, bool ativo, CancellationToken cancellationToken = default)
    {
        var produto = await _dbContext.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId, cancellationToken);
        if (produto is null)
            return false;

        if (produto.Ativo == ativo)
            return true;

        if (ativo)
            produto.Ativar();
        else
            produto.Desativar();

        _auditoria.Registrar(ativo ? AcoesAuditoria.AtivarProduto : AcoesAuditoria.DesativarProduto, nameof(Produto), produto.Id, $"Nome: {produto.Nome}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExcluirAsync(Guid produtoId, CancellationToken cancellationToken = default)
    {
        var produto = await _dbContext.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId, cancellationToken);
        if (produto is null)
            return false;

        // Venda e consumo interno são histórico de dinheiro (faturamento, saldo devedor): esses ficam para sempre.
        var temHistorico = await _dbContext.MovimentosEstoque.AnyAsync(
            m => m.ProdutoId == produtoId && (m.Tipo == TipoMovimentoEstoque.Venda || m.Tipo == TipoMovimentoEstoque.ConsumoInterno),
            cancellationToken);
        if (temHistorico)
            throw new OperacaoCadastroBloqueadaException("Este produto já tem vendas ou consumo lançados. Desative-o em vez de excluir.");

        _dbContext.MovimentosEstoque.RemoveRange(
            await _dbContext.MovimentosEstoque.Where(m => m.ProdutoId == produtoId).ToListAsync(cancellationToken));
        _dbContext.Produtos.Remove(produto);
        _auditoria.Registrar(AcoesAuditoria.ApagarDefinitivo, nameof(Produto), produto.Id, $"Nome: {produto.Nome}");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<ProdutoResumo?> RegistrarEntradaAsync(Guid produtoId, RegistrarEntrada dados, CancellationToken cancellationToken = default) =>
        MovimentarAsync(produtoId, (produto, agora) =>
        {
            var movimento = produto.RegistrarEntrada(
                dados.Quantidade, dados.CustoUnitario, dados.Fornecedor, dados.Observacao, _usuarioAtual.UsuarioId, agora);
            _auditoria.Registrar(AcoesAuditoria.EntradaEstoque, nameof(Produto), produto.Id,
                $"{produto.Nome}: +{dados.Quantidade} ({movimento.QuantidadeAntes} → {movimento.QuantidadeDepois})");
            return movimento;
        }, cancellationToken);

    public Task<ProdutoResumo?> RegistrarAjusteAsync(Guid produtoId, RegistrarAjuste dados, CancellationToken cancellationToken = default) =>
        MovimentarAsync(produtoId, (produto, agora) =>
        {
            var movimento = produto.RegistrarAjuste(dados.QuantidadeContada, dados.Motivo, _usuarioAtual.UsuarioId, agora);
            _auditoria.Registrar(AcoesAuditoria.AjusteEstoque, nameof(Produto), produto.Id,
                $"{produto.Nome}: {movimento.QuantidadeAntes} → {movimento.QuantidadeDepois}. Motivo: {movimento.Observacao}");
            return movimento;
        }, cancellationToken);

    /// <summary>
    /// Todo movimento roda com a linha do produto travada (<c>FOR UPDATE</c>) dentro da transação: duas saídas
    /// simultâneas no limite do estoque são enfileiradas, e a segunda já vê a quantidade que sobrou (mesma
    /// cautela da seção 8.2). O check do banco (quantidade ≥ 0) é a última barreira.
    /// </summary>
    private async Task<ProdutoResumo?> MovimentarAsync(
        Guid produtoId, Func<Produto, DateTimeOffset, MovimentoEstoque> movimentar, CancellationToken cancellationToken)
    {
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var produto = await TravarProdutoAsync(produtoId, cancellationToken);
            if (produto is null)
            {
                await transacao.RollbackAsync(cancellationToken);
                return null;
            }

            _dbContext.MovimentosEstoque.Add(movimentar(produto, DateTimeOffset.UtcNow));
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return Mapear(produto);
        });
    }

    /// <summary>Carrega o produto já travado; os dados vêm do banco depois da trava (nunca de uma leitura anterior).</summary>
    internal async Task<Produto?> TravarProdutoAsync(Guid produtoId, CancellationToken cancellationToken)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM produtos WHERE id = {produtoId} AND negocio_id = {negocioId} FOR UPDATE", cancellationToken);

        var produto = await _dbContext.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId, cancellationToken);
        if (produto is not null)
            await _dbContext.Entry(produto).ReloadAsync(cancellationToken);
        return produto;
    }

    public async Task<IReadOnlyList<MovimentoEstoqueResumo>?> ListarMovimentosAsync(Guid produtoId, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Produtos.AnyAsync(p => p.Id == produtoId, cancellationToken))
            return null;

        var movimentos = await _dbContext.MovimentosEstoque.AsNoTracking()
            .Where(m => m.ProdutoId == produtoId)
            .OrderByDescending(m => m.Data).ThenByDescending(m => m.CriadoEm)
            .ToListAsync(cancellationToken);

        var usuarioIds = movimentos.Where(m => m.UsuarioId is not null).Select(m => m.UsuarioId!.Value).Distinct().ToList();
        var usuarios = usuarioIds.Count == 0
            ? []
            : await _dbContext.Usuarios.AsNoTracking()
                .Where(u => usuarioIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Nome, cancellationToken);

        return movimentos.Select(m => new MovimentoEstoqueResumo(
            m.Id, m.Tipo.ToString(), m.Quantidade, m.ValorUnitario, m.QuantidadeAntes, m.QuantidadeDepois, m.Data,
            m.UsuarioId is not null && usuarios.TryGetValue(m.UsuarioId.Value, out var nome) ? nome : null,
            m.Observacao, m.Fornecedor)).ToList();
    }

    public async Task<AlertasEstoque> ObterAlertasAsync(CancellationToken cancellationToken = default)
    {
        var emAlerta = await _dbContext.Produtos.AsNoTracking()
            .Where(p => p.Ativo && p.QuantidadeEstoque <= p.QuantidadeMinima)
            .OrderBy(p => p.Nome)
            .Select(p => new ProdutoEmAlerta(p.Id, p.Nome, p.QuantidadeEstoque, p.QuantidadeMinima))
            .ToListAsync(cancellationToken);

        return new AlertasEstoque(
            emAlerta.Where(p => p.QuantidadeEstoque > 0).ToList(),
            emAlerta.Where(p => p.QuantidadeEstoque == 0).ToList());
    }

    private static ProdutoResumo Mapear(Produto produto) => new(
        produto.Id, produto.Nome, produto.Categoria, produto.PrecoCusto, produto.PrecoVenda, produto.QuantidadeEstoque,
        produto.QuantidadeMinima, produto.Ativo,
        !produto.Ativo ? "Inativo" : produto.Esgotado ? "Esgotado" : produto.EstoqueBaixo ? "EstoqueBaixo" : "Normal");
}
