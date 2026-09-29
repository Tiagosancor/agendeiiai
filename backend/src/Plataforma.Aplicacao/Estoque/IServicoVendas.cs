namespace Plataforma.Aplicacao.Estoque;

/// <summary>
/// Venda de produto a cliente (seção 7), avulsa ou vinculada a um atendimento. Tudo sob "vender produtos"; nada
/// daqui mostra preço de custo. Cada venda baixa o estoque com a linha do produto travada, como os outros movimentos.
/// </summary>
public interface IServicoVendas
{
    /// <summary>
    /// Produtos à venda e as pessoas que podem ser escolhidas como "Vendido por". <paramref name="agendamentoId"/>
    /// sugere o profissional do atendimento; sem ele, quem está logado.
    /// </summary>
    Task<OpcoesVenda> ObterOpcoesAsync(Guid? agendamentoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lança a venda numa transação só: ou todos os itens baixam o estoque, ou nenhum.
    /// Lança <c>EstoqueInsuficienteException</c> (com a quantidade disponível) e <see cref="ArgumentException"/>.
    /// </summary>
    Task<VendaLancada> LancarAsync(LancarVenda dados, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VendaResumo>> ListarAsync(DateOnly de, DateOnly ate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Desfaz a venda (seção 7, decisão do dono: quem gerencia o estoque): os produtos voltam ao estoque e ela sai do
    /// faturamento e da comissão. Falso: não encontrada. Lança <see cref="ArgumentException"/> sem motivo,
    /// <see cref="InvalidOperationException"/> se já estornada e <c>QuinzenaFechadaException</c> se a comissão do vendedor
    /// já entrou numa quinzena fechada.
    /// </summary>
    Task<bool> EstornarAsync(Guid vendaId, string? motivo, CancellationToken cancellationToken = default);
}

public sealed record EstornarVenda(string? Motivo);

public sealed record ProdutoAVenda(Guid Id, string Nome, string? Categoria, decimal PrecoVenda, int QuantidadeEstoque);

/// <summary>Um "Vendido por" possível: um profissional ativo, ou um usuário com "vender produtos" sem cadastro de profissional.</summary>
public sealed record VendedorOpcao(Guid? ProfissionalId, Guid? UsuarioId, string Nome);

public sealed record OpcoesVenda(IReadOnlyList<ProdutoAVenda> Produtos, IReadOnlyList<VendedorOpcao> Vendedores, VendedorOpcao? VendedorSugerido);

public sealed record ItemLancarVenda(Guid ProdutoId, int Quantidade, decimal ValorUnitario);

public sealed record NovoClienteVenda(string Nome, string? Telefone);

/// <summary>
/// Vendedor: exatamente um entre <c>VendedorProfissionalId</c> e <c>VendedorUsuarioId</c>. Cliente opcional: um existente,
/// um cadastro rápido (nome e telefone opcional, como no encaixe) ou nenhum. Com <c>AgendamentoId</c>, o cliente é o do atendimento.
/// </summary>
public sealed record LancarVenda(
    IReadOnlyList<ItemLancarVenda> Itens, Guid? VendedorProfissionalId, Guid? VendedorUsuarioId,
    Guid? AgendamentoId = null, Guid? ClienteId = null, NovoClienteVenda? NovoCliente = null);

public sealed record VendaLancada(Guid VendaId, decimal Total);

public sealed record ItemVendaResumo(Guid ProdutoId, string Produto, int Quantidade, decimal ValorUnitario, decimal Total);

/// <summary><c>Estornada</c>: desfeita — fica na lista para o histórico, mas fora dos totais.</summary>
public sealed record VendaResumo(
    Guid Id, DateTimeOffset Data, string? Cliente, Guid? AgendamentoId, string Vendedor, decimal Total, IReadOnlyList<ItemVendaResumo> Itens,
    bool Estornada = false, string? MotivoEstorno = null);
