using FluentAssertions;
using Plataforma.Dominio.Comissoes;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class PeriodoComissaoTestes
{
    [Theory]
    [InlineData("2026-09-15", "2026-09-16", "2026-09-30")]
    [InlineData("2026-09-30", "2026-10-01", "2026-10-15")]
    [InlineData("2026-02-15", "2026-02-16", "2026-02-28")]
    [InlineData("2028-02-15", "2028-02-16", "2028-02-29")]
    [InlineData("2026-12-31", "2027-01-01", "2027-01-15")]
    [InlineData("2026-09-10", "2026-09-11", "2026-09-15")] // último fora do padrão: completa a quinzena em curso
    public void Sugestao_e_a_quinzena_seguinte_a_ultima(string fimDoUltimo, string inicio, string fim)
    {
        PeriodoComissao.SugerirProximo(DateOnly.Parse(fimDoUltimo), new DateOnly(2026, 1, 1))
            .Should().Be((DateOnly.Parse(inicio), DateOnly.Parse(fim)));
    }

    [Theory]
    [InlineData("2026-09-03", "2026-09-01", "2026-09-15")]
    [InlineData("2026-09-20", "2026-09-16", "2026-09-30")]
    public void Sem_quinzena_anterior_sugere_a_que_contem_hoje(string hoje, string inicio, string fim)
    {
        PeriodoComissao.SugerirProximo(null, DateOnly.Parse(hoje)).Should().Be((DateOnly.Parse(inicio), DateOnly.Parse(fim)));
    }

    [Fact]
    public void Inicio_depois_do_fim_e_recusado() =>
        FluentActions.Invoking(() => PeriodoComissao.Criar(Guid.NewGuid(), new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 15)))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void Fechada_nao_muda_datas_e_reabrir_volta_a_aberta()
    {
        var periodo = PeriodoComissao.Criar(Guid.NewGuid(), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15));
        periodo.Fechar(Guid.NewGuid(), DateTimeOffset.UtcNow);

        periodo.Invoking(p => p.AlterarDatas(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14))).Should().Throw<InvalidOperationException>();
        periodo.Invoking(p => p.Fechar(null, DateTimeOffset.UtcNow)).Should().Throw<InvalidOperationException>();

        periodo.Reabrir();

        periodo.Estado.Should().Be(EstadoPeriodoComissao.Aberta);
        periodo.FechadoEm.Should().BeNull();
        periodo.Contem(new DateOnly(2026, 9, 15)).Should().BeTrue();
        periodo.Contem(new DateOnly(2026, 9, 16)).Should().BeFalse();
    }
}
