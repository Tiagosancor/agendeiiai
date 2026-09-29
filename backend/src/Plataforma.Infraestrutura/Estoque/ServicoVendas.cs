using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comissoes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Estoque;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Comissoes;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Estoque;

public sealed class ServicoVendas : IServicoVendas
{
    public const int MaximoItens = 50;

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IRegistroAuditoria _auditoria;
    private readonly TravaQuinzenas _travaQuinzenas;

    public ServicoVendas(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IUsuarioAtual usuarioAtual, IRegistroAuditoria auditoria,
        TravaQuinzenas travaQuinzenas)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _usuarioAtual = usuarioAtual;
        _auditoria = auditoria;
        _travaQuinzenas = travaQuinzenas;
    }

    public async Task<OpcoesVenda> ObterOpcoesAsync(Guid? agendamentoId, CancellationToken cancellationToken = default)
    {
        var produtos = await _dbContext.Produtos.AsNoTracking()
            .Where(p => p.Ativo)
            .OrderBy(p => p.Nome)
            .Select(p => new ProdutoAVenda(p.Id, p.Nome, p.Categoria, p.PrecoVenda, p.QuantidadeEstoque))
            .ToListAsync(cancellationToken);

        var vendedores = await ListarVendedoresAsync(cancellationToken);

        // Sugestão (sempre editável na tela): o profissional do atendimento; sem atendimento, quem está logado.
        VendedorOpcao? sugerido = null;
        if (agendamentoId is not null)
        {
            var profissionalId = await _dbContext.Agendamentos.AsNoTracking()
                .Where(a => a.Id == agendamentoId)
                .Select(a => (Guid?)a.ProfissionalId)
                .FirstOrDefaultAsync(cancellationToken);
            sugerido = vendedores.FirstOrDefault(v => v.ProfissionalId is not null && v.ProfissionalId == profissionalId);
        }

        if (sugerido is null && _usuarioAtual.UsuarioId is { } usuarioId)
        {
            var vinculo = await _dbContext.Usuarios.AsNoTracking()
                .Where(u => u.Id == usuarioId)
                .Select(u => u.ProfissionalId)
                .FirstOrDefaultAsync(cancellationToken);
            sugerido = vinculo is not null
                ? vendedores.FirstOrDefault(v => v.ProfissionalId == vinculo)
                : vendedores.FirstOrDefault(v => v.UsuarioId == usuarioId);
        }

        return new OpcoesVenda(produtos, vendedores, sugerido);
    }

    /// <summary>
    /// Profissionais ativos (o do atendimento pode vender, mesmo sem login) e usuários ativos com "vender produtos" sem
    /// cadastro de profissional. Quem tem os dois aparece uma vez só, como profissional (é de lá que vem o percentual).
    /// </summary>
    private async Task<List<VendedorOpcao>> ListarVendedoresAsync(CancellationToken cancellationToken)
    {
        var profissionais = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Ativo && !p.Excluido)
            .Select(p => new VendedorOpcao(p.Id, null, p.Nome))
            .ToListAsync(cancellationToken);

        var usuarios = await _dbContext.Usuarios.AsNoTracking()
            .Where(u => u.Ativo && !u.Excluido && u.ProfissionalId == null
                && u.Permissoes.Any(p => p.Permissao == Permissao.VenderProdutos))
            .Select(u => new VendedorOpcao(null, u.Id, u.Nome))
            .ToListAsync(cancellationToken);

        return profissionais.Concat(usuarios).OrderBy(v => v.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<VendaLancada> LancarAsync(LancarVenda dados, CancellationToken cancellationToken = default)
    {
        if (dados.Itens is null || dados.Itens.Count == 0)
            throw new ArgumentException("Inclua ao menos um produto.");
        if (dados.Itens.Count > MaximoItens)
            throw new ArgumentException($"No máximo {MaximoItens} produtos por venda.");

        var vendedor = await ResolverVendedorAsync(dados, cancellationToken);

        Guid? clienteId;
        if (dados.AgendamentoId is { } agendamentoId)
        {
            var agendamento = await _dbContext.Agendamentos.AsNoTracking()
                .Where(a => a.Id == agendamentoId)
                .Select(a => new { a.Status, a.ClienteId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new ArgumentException("Atendimento não encontrado.");
            if (agendamento.Status is not (StatusAgendamento.Agendado or StatusAgendamento.EmAtendimento or StatusAgendamento.Concluido))
                throw new ArgumentException("Só dá para vender dentro de um atendimento agendado, em andamento ou concluído.");
            clienteId = agendamento.ClienteId;
        }
        else
        {
            clienteId = await ResolverClienteAsync(dados, cancellationToken);
        }

        var negocioId = _contextoNegocio.NegocioId!.Value;
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var venda = VendaProduto.Criar(negocioId, DateTimeOffset.UtcNow, clienteId, dados.AgendamentoId, vendedor, _usuarioAtual.UsuarioId);

            // Trava em ordem de ID: duas vendas com os mesmos produtos em ordem diferente não se travam mutuamente.
            foreach (var item in dados.Itens.OrderBy(i => i.ProdutoId))
            {
                var produto = await TravaProduto.TravarAsync(_dbContext, negocioId, item.ProdutoId, cancellationToken)
                    ?? throw new ArgumentException("Produto não encontrado.");
                _dbContext.MovimentosEstoque.Add(venda.AdicionarItem(produto, item.Quantidade, item.ValorUnitario));
            }

            _dbContext.VendasProduto.Add(venda);
            _auditoria.Registrar(AcoesAuditoria.VendaProduto, nameof(VendaProduto), venda.Id,
                $"{string.Join(", ", venda.Itens.Select(i => $"{i.NomeProduto} × {i.Quantidade}"))}; total {FormatacaoBrasil.Reais(venda.Total)}; "
                + $"vendido por {venda.VendedorNome}" + (venda.AgendamentoId is null ? "" : "; no atendimento"));

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return new VendaLancada(venda.Id, venda.Total);
        });
    }

    private async Task<Vendedor> ResolverVendedorAsync(LancarVenda dados, CancellationToken cancellationToken)
    {
        const string invalido = "Escolha quem vendeu entre as pessoas habilitadas.";
        if (dados.VendedorProfissionalId is null == dados.VendedorUsuarioId is null)
            throw new ArgumentException("Informe quem vendeu (uma pessoa só).");

        var profissionalId = dados.VendedorProfissionalId;
        if (dados.VendedorUsuarioId is { } usuarioId)
        {
            var usuario = await _dbContext.Usuarios.AsNoTracking()
                .Where(u => u.Id == usuarioId && u.Ativo && !u.Excluido && u.Permissoes.Any(p => p.Permissao == Permissao.VenderProdutos))
                .Select(u => new { u.Id, u.Nome, u.ProfissionalId, u.PercentualComissaoProdutoVenda })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new ArgumentException(invalido);

            if (usuario.ProfissionalId is null)
                return new Vendedor(null, usuario.Id, usuario.Nome, usuario.PercentualComissaoProdutoVenda);

            // Usuário vinculado a um profissional: a comissão de produto é a do profissional.
            profissionalId = usuario.ProfissionalId;
        }

        var profissional = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Id == profissionalId && p.Ativo && !p.Excluido)
            .Select(p => new { p.Id, p.Nome, p.PercentualComissaoProdutoVenda })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException(invalido);

        return new Vendedor(profissional.Id, null, profissional.Nome, profissional.PercentualComissaoProdutoVenda);
    }

    /// <summary>Mesmo cadastro rápido do encaixe: telefone já cadastrado reaproveita o cliente.</summary>
    private async Task<Guid?> ResolverClienteAsync(LancarVenda dados, CancellationToken cancellationToken)
    {
        if (dados.ClienteId is { } clienteId)
        {
            if (!await _dbContext.Clientes.AnyAsync(c => c.Id == clienteId && !c.Excluido, cancellationToken))
                throw new ArgumentException("Cliente não encontrado.");
            return clienteId;
        }

        if (dados.NovoCliente is null)
            return null;
        if (string.IsNullOrWhiteSpace(dados.NovoCliente.Nome))
            throw new ArgumentException("Informe o nome do cliente.");

        TelefoneE164? telefone = null;
        if (!string.IsNullOrWhiteSpace(dados.NovoCliente.Telefone))
        {
            try
            {
                telefone = TelefoneE164.Criar(dados.NovoCliente.Telefone);
            }
            catch (ArgumentException)
            {
                throw new ArgumentException("Telefone inválido. Informe o DDD e o número.");
            }

            var existente = await _dbContext.Clientes.FirstOrDefaultAsync(c => c.Telefone == telefone, cancellationToken);
            if (existente is not null)
                return existente.Id;
        }

        var novo = Cliente.Criar(_contextoNegocio.NegocioId!.Value, dados.NovoCliente.Nome, telefone, OrigemCliente.Encaixe);
        _dbContext.Clientes.Add(novo);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            _dbContext.Entry(novo).State = EntityState.Detached;
            return (await _dbContext.Clientes.FirstAsync(c => c.Telefone == telefone, cancellationToken)).Id;
        }

        return novo.Id;
    }

    public async Task<IReadOnlyList<VendaResumo>> ListarAsync(DateOnly de, DateOnly ate, CancellationToken cancellationToken = default)
    {
        var (inicioUtc, fimUtc) = ConsultaLinhasComissao.IntervaloUtc(de, ate, await _travaQuinzenas.FusoAsync(cancellationToken));

        var vendas = await _dbContext.VendasProduto.AsNoTracking()
            .Include(v => v.Itens)
            .Where(v => v.Data >= inicioUtc && v.Data < fimUtc)
            .OrderByDescending(v => v.Data)
            .ToListAsync(cancellationToken);

        var idsClientes = vendas.Where(v => v.ClienteId != null).Select(v => v.ClienteId!.Value).Distinct().ToList();
        var clientes = await _dbContext.Clientes.AsNoTracking()
            .Where(c => idsClientes.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nome, cancellationToken);

        return vendas.Select(v => new VendaResumo(
            v.Id, v.Data, v.ClienteId is { } c && clientes.TryGetValue(c, out var nome) ? nome : null, v.AgendamentoId, v.VendedorNome, v.Total,
            v.Itens.OrderBy(i => i.NomeProduto)
                .Select(i => new ItemVendaResumo(i.ProdutoId, i.NomeProduto, i.Quantidade, i.ValorUnitario, i.Total)).ToList(),
            v.Estornada, v.MotivoEstorno)).ToList();
    }

    public async Task<bool> EstornarAsync(Guid vendaId, string? motivo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("Informe o motivo do estorno.");

        var negocioId = _contextoNegocio.NegocioId!.Value;
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            // A comissão da venda pode ter entrado no fechamento de uma quinzena: mesma trava de concluir/reabrir atendimento.
            await _travaQuinzenas.TravarNegocioAsync(cancellationToken);
            var venda = await _dbContext.VendasProduto.Include(v => v.Itens).FirstOrDefaultAsync(v => v.Id == vendaId, cancellationToken);
            if (venda is null)
                return false;
            if (venda.VendedorProfissionalId is { } profissionalId
                && await _travaQuinzenas.AtendimentoTravadoAsync(profissionalId, venda.Data, cancellationToken))
                throw new QuinzenaFechadaException(
                    "A comissão desta venda já entrou numa quinzena fechada. Para estornar, o Administrador precisa reabrir a quinzena.");

            var produtos = new Dictionary<Guid, Produto>();
            foreach (var produtoId in venda.Itens.Select(i => i.ProdutoId).Distinct().OrderBy(id => id))
                produtos[produtoId] = await TravaProduto.TravarAsync(_dbContext, negocioId, produtoId, cancellationToken)
                    ?? throw new InvalidOperationException("Produto da venda não encontrado.");

            _dbContext.MovimentosEstoque.AddRange(venda.Estornar(produtos, motivo, _usuarioAtual.UsuarioId, DateTimeOffset.UtcNow));
            _auditoria.Registrar(AcoesAuditoria.EstornarVenda, nameof(VendaProduto), venda.Id,
                $"{string.Join(", ", venda.Itens.Select(i => $"{i.NomeProduto} × {i.Quantidade}"))} voltam ao estoque; "
                + $"{FormatacaoBrasil.Reais(venda.Total)} sai do faturamento e da comissão de {venda.VendedorNome}. Motivo: {venda.MotivoEstorno}");

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException("Esta venda acabou de ser estornada por outra pessoa.");
            }

            await transacao.CommitAsync(cancellationToken);
            return true;
        });
    }
}
