using FluentAssertions;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comissoes;
using Plataforma.Dominio.Estoque;

namespace Plataforma.Testes.Unidade.Dominio;

/// <summary>
/// B7.1 — o Id do que se grava é gerado fora do callback repetível da estratégia: as fábricas aceitam esse Id, e sem ele
/// (outra requisição) cada criação continua recebendo um novo.
/// </summary>
public class IdentificadorEstavelTestes
{
    private static readonly Guid Negocio = Guid.NewGuid();
    private static readonly DateTimeOffset Agora = DateTimeOffset.UtcNow;

    private static Produto ProdutoComEstoque(int quantidade)
    {
        var produto = Produto.Criar(Negocio, "Pomada", null, 5m, 20m);
        produto.RegistrarEntrada(quantidade, 5m, null, null, null, Agora);
        return produto;
    }

    private static readonly Vendedor Vendedor = new(Guid.NewGuid(), null, "Ana", 10m);

    [Fact]
    public void Venda_usa_o_id_informado_e_sem_id_gera_um_novo_a_cada_vez()
    {
        var id = Guid.NewGuid();

        VendaProduto.Criar(Negocio, Agora, null, null, Vendedor, null, id).Id.Should().Be(id);
        VendaProduto.Criar(Negocio, Agora, null, null, Vendedor, null).Id
            .Should().NotBe(VendaProduto.Criar(Negocio, Agora, null, null, Vendedor, null).Id);
    }

    [Fact]
    public void Itens_da_venda_apontam_para_o_id_informado()
    {
        var id = Guid.NewGuid();
        var venda = VendaProduto.Criar(Negocio, Agora, null, null, Vendedor, null, id);

        venda.AdicionarItem(ProdutoComEstoque(5), 1, 20m);

        venda.Itens.Should().ContainSingle().Which.VendaId.Should().Be(id);
    }

    [Fact]
    public void Entrada_ajuste_e_saida_usam_o_id_do_movimento_informado()
    {
        var produto = ProdutoComEstoque(10);
        var entrada = Guid.NewGuid();
        var saida = Guid.NewGuid();
        var ajuste = Guid.NewGuid();

        produto.RegistrarEntrada(5, 5m, null, null, null, Agora, entrada).Id.Should().Be(entrada);
        produto.RegistrarSaida(TipoMovimentoEstoque.ConsumoInterno, 1, 5m, null, null, Agora, saida).Id.Should().Be(saida);
        produto.RegistrarAjuste(3, "Contagem", null, Agora, ajuste).Id.Should().Be(ajuste);
    }

    [Fact]
    public void Movimento_sem_id_informado_recebe_um_id_novo()
    {
        var produto = ProdutoComEstoque(10);

        produto.RegistrarEntrada(1, 5m, null, null, null, Agora).Id
            .Should().NotBe(produto.RegistrarEntrada(1, 5m, null, null, null, Agora).Id);
    }

    [Fact]
    public void Consumo_usa_o_id_informado_no_lancamento()
    {
        var id = Guid.NewGuid();

        var (lancamento, _) = LancamentoSaldoDevedor.CriarConsumo(
            PessoaComissao.Criar(Guid.NewGuid(), null), ProdutoComEstoque(5), 2, 5m, DateOnly.FromDateTime(DateTime.UtcNow), null, null, Agora, id);

        lancamento.Id.Should().Be(id);
    }

    [Fact]
    public void Agendamento_confirmado_e_reserva_usam_o_id_informado_inclusive_nos_servicos()
    {
        var id = Guid.NewGuid();
        var servicos = new[] { new ItemServicoAgendamento(Guid.NewGuid(), "Corte", 50m, 30) };

        var confirmado = Agendamento.CriarConfirmado(Negocio, Guid.NewGuid(), Guid.NewGuid(), Agora.AddDays(1), servicos, id: id);
        var reserva = Agendamento.CriarReserva(Negocio, Guid.NewGuid(), null, Agora.AddDays(1), servicos, Agora, TimeSpan.FromMinutes(10), id);

        confirmado.Id.Should().Be(id);
        confirmado.Servicos.Should().OnlyContain(s => s.AgendamentoId == id);
        reserva.Id.Should().Be(id);
        Agendamento.CriarConfirmado(Negocio, Guid.NewGuid(), Guid.NewGuid(), Agora.AddDays(1), servicos).Id.Should().NotBe(id);
    }
}
