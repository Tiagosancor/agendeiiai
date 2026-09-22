using FluentAssertions;
using Plataforma.Dominio.Cupons;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class CupomTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Criar_normaliza_o_codigo_para_maiuscula()
    {
        var cupom = Cupom.Criar(Guid.NewGuid(), "  bemvindo10  ", TipoDescontoCupom.Percentual, 10m);

        cupom.Codigo.Should().Be("BEMVINDO10");
    }

    [Fact]
    public void Criar_com_desconto_percentual_acima_de_100_lanca_excecao()
    {
        var acao = () => Cupom.Criar(Guid.NewGuid(), "X", TipoDescontoCupom.Percentual, 150m);
        acao.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TentarAplicar_percentual_calcula_o_desconto_sobre_o_total()
    {
        var cupom = Cupom.Criar(Guid.NewGuid(), "10OFF", TipoDescontoCupom.Percentual, 10m);

        var resultado = cupom.TentarAplicar(100m, [], Agora);

        resultado.Sucesso.Should().BeTrue();
        resultado.Desconto.Should().Be(10m);
    }

    [Fact]
    public void TentarAplicar_valor_fixo_nunca_deixa_o_total_negativo()
    {
        var cupom = Cupom.Criar(Guid.NewGuid(), "50OFF", TipoDescontoCupom.ValorFixo, 50m);

        var resultado = cupom.TentarAplicar(30m, [], Agora);

        resultado.Sucesso.Should().BeTrue();
        resultado.Desconto.Should().Be(30m);
    }

    [Fact]
    public void TentarAplicar_expirado_falha()
    {
        var cupom = Cupom.Criar(Guid.NewGuid(), "X", TipoDescontoCupom.Percentual, 10m, validoAte: Agora.AddDays(-1));

        var resultado = cupom.TentarAplicar(100m, [], Agora);

        resultado.Sucesso.Should().BeFalse();
    }

    [Fact]
    public void TentarAplicar_esgotado_falha()
    {
        var cupom = Cupom.Criar(Guid.NewGuid(), "X", TipoDescontoCupom.Percentual, 10m, limiteUsos: 1);
        cupom.RegistrarUso();

        var resultado = cupom.TentarAplicar(100m, [], Agora);

        resultado.Sucesso.Should().BeFalse();
    }

    [Fact]
    public void TentarAplicar_fora_do_escopo_de_servicos_falha()
    {
        var servicoPermitido = Guid.NewGuid();
        var cupom = Cupom.Criar(Guid.NewGuid(), "X", TipoDescontoCupom.Percentual, 10m, servicoIdsEscopo: [servicoPermitido]);

        var resultado = cupom.TentarAplicar(100m, [Guid.NewGuid()], Agora);

        resultado.Sucesso.Should().BeFalse();
    }

    [Fact]
    public void TentarAplicar_dentro_do_escopo_de_servicos_funciona()
    {
        var servicoPermitido = Guid.NewGuid();
        var cupom = Cupom.Criar(Guid.NewGuid(), "X", TipoDescontoCupom.Percentual, 10m, servicoIdsEscopo: [servicoPermitido]);

        var resultado = cupom.TentarAplicar(100m, [servicoPermitido, Guid.NewGuid()], Agora);

        resultado.Sucesso.Should().BeTrue();
    }

    [Fact]
    public void Desativar_faz_TentarAplicar_falhar()
    {
        var cupom = Cupom.Criar(Guid.NewGuid(), "X", TipoDescontoCupom.Percentual, 10m);
        cupom.Desativar();

        cupom.TentarAplicar(100m, [], Agora).Sucesso.Should().BeFalse();
    }
}
