using System.Text.Json;
using FluentAssertions;
using Plataforma.Infraestrutura.Idempotencia;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>Hash canônico da intenção da requisição (Idempotency-Key): determinístico, insensível a forma, sensível a conteúdo.</summary>
public class HashDeRequisicaoTestes
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed record Item(Guid ProdutoId, int Quantidade, decimal Valor);

    private sealed record Venda(IReadOnlyList<Item> Itens, Guid? ClienteId, string? Observacao, DateTimeOffset? Quando);

    private static string Hash(string json, IReadOnlyDictionary<string, object?>? rota = null) =>
        HashDeRequisicao.Calcular(new SortedDictionary<string, object?> { ["dados"] = JsonSerializer.Deserialize<Venda>(json, Web) }, rota);

    private static readonly Guid Produto = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string Base(string valor = "20", string quantidade = "2", string quando = "2026-10-12T10:00:00-03:00", string observacao = "\"x\"") =>
        $$"""{"itens":[{"produtoId":"{{Produto}}","quantidade":{{quantidade}},"valor":{{valor}}}],"clienteId":null,"observacao":{{observacao}},"quando":"{{quando}}"}""";

    [Fact]
    public void Mesmo_dto_gera_o_mesmo_hash()
    {
        Hash(Base()).Should().Be(Hash(Base())).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Ordem_das_propriedades_e_espacos_nao_mudam_o_hash()
    {
        var reordenado = $$"""
            {
              "quando": "2026-10-12T10:00:00-03:00",
              "observacao": "x",
              "clienteId": null,
              "itens": [ { "valor": 20, "quantidade": 2, "produtoId": "{{Produto}}" } ]
            }
            """;

        Hash(reordenado).Should().Be(Hash(Base()));
    }

    [Fact]
    public void Escala_do_decimal_nao_muda_o_hash_mas_o_valor_sim()
    {
        Hash(Base(valor: "20.00")).Should().Be(Hash(Base(valor: "20")));
        Hash(Base(valor: "20.0")).Should().Be(Hash(Base(valor: "2E1")));
        Hash(Base(valor: "20.01")).Should().NotBe(Hash(Base(valor: "20")));
    }

    [Fact]
    public void Mesmo_instante_em_fusos_diferentes_tem_o_mesmo_hash()
    {
        Hash(Base(quando: "2026-10-12T13:00:00Z")).Should().Be(Hash(Base(quando: "2026-10-12T10:00:00-03:00")));
        Hash(Base(quando: "2026-10-12T13:00:01Z")).Should().NotBe(Hash(Base(quando: "2026-10-12T10:00:00-03:00")));
    }

    [Fact]
    public void Corpo_realmente_diferente_muda_o_hash()
    {
        var referencia = Hash(Base());

        Hash(Base(quantidade: "3")).Should().NotBe(referencia);
        Hash(Base(observacao: "\"y\"")).Should().NotBe(referencia);
        Hash(Base(observacao: "null")).Should().NotBe(referencia);
    }

    [Fact]
    public void Valores_de_rota_entram_no_hash_e_a_ordem_deles_nao()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var comA = Hash(Base(), new Dictionary<string, object?> { ["id"] = a });
        var comB = Hash(Base(), new Dictionary<string, object?> { ["id"] = b });
        comA.Should().NotBe(comB);

        Hash(Base(), new Dictionary<string, object?> { ["x"] = 1, ["id"] = a })
            .Should().Be(Hash(Base(), new Dictionary<string, object?> { ["id"] = a, ["x"] = 1 }));
    }

    [Fact]
    public void Id_derivado_e_estavel_e_muda_com_qualquer_parte_do_escopo()
    {
        var negocio = Guid.NewGuid();
        var referencia = HashDeRequisicao.IdDerivado(negocio, "u:1", "POST painel/vendas", "chave-1234567890123", "cliente");

        HashDeRequisicao.IdDerivado(negocio, "u:1", "POST painel/vendas", "chave-1234567890123", "cliente").Should().Be(referencia);
        HashDeRequisicao.IdDerivado(Guid.NewGuid(), "u:1", "POST painel/vendas", "chave-1234567890123", "cliente").Should().NotBe(referencia);
        HashDeRequisicao.IdDerivado(negocio, "u:2", "POST painel/vendas", "chave-1234567890123", "cliente").Should().NotBe(referencia);
        HashDeRequisicao.IdDerivado(negocio, "u:1", "POST painel/encaixes", "chave-1234567890123", "cliente").Should().NotBe(referencia);
        HashDeRequisicao.IdDerivado(negocio, "u:1", "POST painel/vendas", "outra-chave-123456789", "cliente").Should().NotBe(referencia);
        HashDeRequisicao.IdDerivado(negocio, "u:1", "POST painel/vendas", "chave-1234567890123", "outra").Should().NotBe(referencia);
    }
}
