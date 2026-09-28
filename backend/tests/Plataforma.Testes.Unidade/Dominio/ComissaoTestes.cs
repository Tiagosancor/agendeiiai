using FluentAssertions;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comissoes;
using Plataforma.Dominio.Profissionais;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

/// <summary>Comissão por linha de serviço (seção 7).</summary>
public sealed class ComissaoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sem_cupom_cada_linha_recebe_o_percentual_do_proprio_preco()
    {
        var linhas = CalculadoraComissao.Calcular([50m, 30m], 0m, 40m);

        linhas.Should().Equal(new ComissaoDaLinha(50m, 20m), new ComissaoDaLinha(30m, 12m));
    }

    [Fact]
    public void Cupom_e_dividido_proporcionalmente_entre_os_servicos()
    {
        // R$ 80 + R$ 20, cupom de R$ 10 → R$ 8 e R$ 2 de desconto.
        var linhas = CalculadoraComissao.Calcular([80m, 20m], 10m, 50m);

        linhas.Should().Equal(new ComissaoDaLinha(72m, 36m), new ComissaoDaLinha(18m, 9m));
    }

    [Fact]
    public void Valores_que_nao_dividem_exato_arredondam_em_centavos_sem_passar_da_comissao_do_total()
    {
        // 3 × R$ 10,00 com cupom de R$ 10,00: R$ 3,33 + 3,33 + 3,34 de desconto → bases 6,67 + 6,67 + 6,66.
        // Comissão do total: 33,33% de R$ 20,00 = R$ 6,666 → R$ 6,67.
        var linhas = CalculadoraComissao.Calcular([10m, 10m, 10m], 10m, 33.33m);

        linhas.Sum(l => l.ValorBase).Should().Be(20m);
        linhas.Sum(l => l.Comissao).Should().Be(6.67m);
        linhas.Should().OnlyContain(l => decimal.Round(l.ValorBase, 2) == l.ValorBase && decimal.Round(l.Comissao, 2) == l.Comissao);
    }

    [Theory]
    [InlineData(new[] { 19.99, 7.49, 12.35 }, 5.55, 17.5)]
    [InlineData(new[] { 0.01, 0.01, 0.01 }, 0, 50)]
    [InlineData(new[] { 100.0, 0.0 }, 30, 7.77)]
    [InlineData(new[] { 33.33, 33.33, 33.34 }, 0.01, 99.99)]
    public void Soma_das_linhas_e_sempre_a_comissao_do_total(double[] precos, double desconto, double percentual)
    {
        var precosDecimais = precos.Select(p => (decimal)p).ToList();
        var linhas = CalculadoraComissao.Calcular(precosDecimais, (decimal)desconto, (decimal)percentual);

        var totalCobrado = precosDecimais.Sum() - (decimal)desconto;
        linhas.Sum(l => l.ValorBase).Should().Be(totalCobrado);
        linhas.Sum(l => l.Comissao).Should().Be(decimal.Round(totalCobrado * (decimal)percentual / 100m, 2, MidpointRounding.AwayFromZero));
        linhas.Should().OnlyContain(l => l.Comissao >= 0 && l.ValorBase >= 0);
    }

    [Fact]
    public void Cupom_maior_que_o_total_zera_sem_ficar_negativo()
    {
        var linhas = CalculadoraComissao.Calcular([10m], 25m, 50m);

        linhas.Should().Equal(new ComissaoDaLinha(0m, 0m));
    }

    [Fact]
    public void Concluir_grava_o_retrato_e_reabrir_estorna()
    {
        var profissionalId = Guid.NewGuid();
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), profissionalId, Guid.NewGuid(), Agora,
            [new ItemServicoAgendamento(Guid.NewGuid(), "Corte", 60m, 30)]);

        agendamento.MarcarConcluido(25m, Agora);

        var linha = agendamento.Servicos.Single();
        (linha.ComissaoProfissionalId, linha.ComissaoValorBase, linha.ComissaoPercentual, linha.ComissaoValor, linha.ComissaoCalculadaEm)
            .Should().Be((profissionalId, 60m, 25m, 15m, Agora));

        agendamento.Reabrir();

        agendamento.Status.Should().Be(StatusAgendamento.Agendado);
        linha.ComissaoValor.Should().BeNull();
        linha.ComissaoPercentual.Should().BeNull();
    }

    [Fact]
    public void So_concluido_pode_ser_reaberto()
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Agora,
            [new ItemServicoAgendamento(Guid.NewGuid(), "Corte", 60m, 30)]);

        agendamento.Invoking(a => a.Reabrir()).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(10.555)]
    public void Percentual_fora_de_0_a_100_ou_com_mais_de_duas_casas_e_recusado(double percentual)
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Ana");

        profissional.Invoking(p => p.DefinirPercentualComissao((decimal)percentual)).Should().Throw<ArgumentException>();
        profissional.PercentualComissao.Should().Be(0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(12.5)]
    public void Percentual_valido_e_aceito(double percentual)
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Ana");

        profissional.DefinirPercentualComissao((decimal)percentual);

        profissional.PercentualComissao.Should().Be((decimal)percentual);
    }
}
