using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Estoque;

/// <summary>
/// Venda de produto a cliente (seção 7): avulsa, no balcão, ou vinculada a um atendimento. Cada item baixa o estoque
/// por um <see cref="MovimentoEstoque"/> de venda e soma ao faturamento na data da venda. Tem <b>um único vendedor</b>
/// — um profissional ou um usuário sem cadastro de profissional —, que recebe a comissão de produto com o percentual
/// vigente no momento, gravado aqui (mudar o percentual depois não altera vendas já feitas).
/// </summary>
public class VendaProduto : EntidadeBase, IEntidadeDoNegocio
{
    private readonly List<ItemVendaProduto> _itens = [];

    public Guid NegocioId { get; private set; }

    public DateTimeOffset Data { get; private set; }

    public Guid? ClienteId { get; private set; }

    /// <summary>Atendimento em que a venda foi lançada ("Produtos vendidos" do "Concluir atendimento"); nulo na avulsa.</summary>
    public Guid? AgendamentoId { get; private set; }

    /// <summary>Vendedor profissional: a comissão vem do percentual de produto do <c>Profissional</c>.</summary>
    public Guid? VendedorProfissionalId { get; private set; }

    /// <summary>Vendedor sem cadastro de profissional (ex.: Recepcionista): percentual do <c>Usuario</c>.</summary>
    public Guid? VendedorUsuarioId { get; private set; }

    /// <summary>Retrato do nome, para o histórico continuar legível se o cadastro mudar.</summary>
    public string VendedorNome { get; private set; } = string.Empty;

    public decimal Total { get; private set; }

    public decimal PercentualComissao { get; private set; }

    public decimal ValorComissao { get; private set; }

    public Guid? LancadaPorUsuarioId { get; private set; }

    /// <summary>
    /// Estorno (venda desfeita): os produtos voltam ao estoque e a venda sai do faturamento e da comissão, mas a linha
    /// fica para o histórico, com quem estornou, quando e por quê.
    /// </summary>
    public DateTimeOffset? EstornadaEm { get; private set; }

    public Guid? EstornadaPorUsuarioId { get; private set; }

    public string? MotivoEstorno { get; private set; }

    public bool Estornada => EstornadaEm is not null;

    public IReadOnlyCollection<ItemVendaProduto> Itens => _itens.AsReadOnly();

    protected VendaProduto()
    {
    }

    public static VendaProduto Criar(
        Guid negocioId, DateTimeOffset data, Guid? clienteId, Guid? agendamentoId, Vendedor vendedor, Guid? lancadaPorUsuarioId)
    {
        if (vendedor.ProfissionalId is null == vendedor.UsuarioId is null)
            throw new ArgumentException("A venda tem exatamente um vendedor.", nameof(vendedor));
        if (vendedor.PercentualComissao is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(vendedor), "A comissão precisa estar entre 0 e 100%.");

        return new VendaProduto
        {
            NegocioId = negocioId,
            Data = data,
            ClienteId = clienteId,
            AgendamentoId = agendamentoId,
            VendedorProfissionalId = vendedor.ProfissionalId,
            VendedorUsuarioId = vendedor.UsuarioId,
            VendedorNome = vendedor.Nome,
            PercentualComissao = vendedor.PercentualComissao,
            LancadaPorUsuarioId = lancadaPorUsuarioId,
        };
    }

    public const int TamanhoMaximoMotivoEstorno = 500;

    /// <summary>
    /// Desfaz a venda: cada item volta ao estoque por um movimento de entrada ("Estorno de venda") nos produtos já
    /// travados pelo chamador. Motivo obrigatório; estornar duas vezes é recusado.
    /// </summary>
    public IReadOnlyList<MovimentoEstoque> Estornar(
        IReadOnlyDictionary<Guid, Produto> produtos, string motivo, Guid? usuarioId, DateTimeOffset agora)
    {
        if (Estornada)
            throw new InvalidOperationException("Esta venda já foi estornada.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("Informe o motivo do estorno.", nameof(motivo));

        var limpo = motivo.Trim();
        MotivoEstorno = limpo.Length > TamanhoMaximoMotivoEstorno ? limpo[..TamanhoMaximoMotivoEstorno] : limpo;
        EstornadaEm = agora;
        EstornadaPorUsuarioId = usuarioId;

        return _itens
            .Select(item => produtos[item.ProdutoId].RegistrarEntrada(
                item.Quantidade, produtos[item.ProdutoId].PrecoCusto, null, "Estorno de venda", usuarioId, agora))
            .ToList();
    }

    /// <summary>Baixa o estoque do produto (já travado pelo chamador) e acrescenta a linha; recalcula total e comissão.</summary>
    public MovimentoEstoque AdicionarItem(Produto produto, int quantidade, decimal valorUnitario)
    {
        if (produto.NegocioId != NegocioId)
            throw new ArgumentException("Produto de outro negócio.", nameof(produto));
        if (!produto.Ativo)
            throw new ArgumentException($"{produto.Nome} está inativo.", nameof(produto));
        if (valorUnitario < 0)
            throw new ArgumentException("O valor cobrado não pode ser negativo.", nameof(valorUnitario));
        if (_itens.Any(i => i.ProdutoId == produto.Id))
            throw new ArgumentException($"{produto.Nome} aparece duas vezes na venda: some as quantidades numa linha só.", nameof(produto));

        var movimento = produto.RegistrarSaida(TipoMovimentoEstoque.Venda, quantidade, valorUnitario, null, LancadaPorUsuarioId, Data);
        _itens.Add(ItemVendaProduto.Criar(NegocioId, Id, produto, quantidade, decimal.Round(valorUnitario, 2), movimento.Id));

        Total = _itens.Sum(i => i.Total);
        ValorComissao = decimal.Round(Total * PercentualComissao / 100m, 2, MidpointRounding.AwayFromZero);
        return movimento;
    }
}

/// <summary>Quem vendeu, com o percentual de comissão de produto vigente agora.</summary>
public sealed record Vendedor(Guid? ProfissionalId, Guid? UsuarioId, string Nome, decimal PercentualComissao);

/// <summary>Uma linha da venda: produto (nome retratado), quantidade e valor unitário cobrado.</summary>
public class ItemVendaProduto : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid VendaId { get; private set; }

    public Guid ProdutoId { get; private set; }

    public string NomeProduto { get; private set; } = string.Empty;

    public int Quantidade { get; private set; }

    /// <summary>O preço de venda cadastrado ou o editado na hora (desconto pontual).</summary>
    public decimal ValorUnitario { get; private set; }

    public decimal Total { get; private set; }

    public Guid MovimentoEstoqueId { get; private set; }

    protected ItemVendaProduto()
    {
    }

    internal static ItemVendaProduto Criar(
        Guid negocioId, Guid vendaId, Produto produto, int quantidade, decimal valorUnitario, Guid movimentoId) => new()
    {
        NegocioId = negocioId,
        VendaId = vendaId,
        ProdutoId = produto.Id,
        NomeProduto = produto.Nome,
        Quantidade = quantidade,
        ValorUnitario = valorUnitario,
        Total = valorUnitario * quantidade,
        MovimentoEstoqueId = movimentoId,
    };
}
