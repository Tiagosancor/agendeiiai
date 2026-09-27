using FluentAssertions;
using Plataforma.Infraestrutura.Comum;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

public sealed class FormatacaoBrasilTestes
{
    [Theory]
    [InlineData(0, "R$ 0,00")]
    [InlineData(80, "R$ 80,00")]
    [InlineData(49.9, "R$ 49,90")]
    [InlineData(1234.5, "R$ 1.234,50")]
    [InlineData(1234567.89, "R$ 1.234.567,89")]
    public void Reais_usa_virgula_nos_centavos_e_ponto_nos_milhares(decimal valor, string esperado) =>
        FormatacaoBrasil.Reais(valor).Should().Be(esperado);

    [Fact]
    public void DataHora_converte_do_utc_para_o_fuso_do_negocio()
    {
        var instante = new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.Zero);

        FormatacaoBrasil.DataHora(instante, "America/Sao_Paulo").Should().Be("05/10/2026 às 10:00");
    }

    [Fact]
    public void Data_usa_o_dia_local_mesmo_quando_em_utc_ja_e_o_dia_seguinte()
    {
        // 01:30 UTC do dia 6 ainda é 22:30 do dia 5 em Brasília.
        var instante = new DateTimeOffset(2026, 10, 6, 1, 30, 0, TimeSpan.Zero);

        FormatacaoBrasil.Data(instante, "America/Sao_Paulo").Should().Be("05/10/2026");
    }
}
