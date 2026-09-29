using FluentAssertions;
using Plataforma.Dominio.Assinaturas;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

/// <summary>Regras da assinatura usadas pela cobrança automática (gateway Asaas): cancelamento, vencida e estorno.</summary>
public sealed class AssinaturaGatewayTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static Plano Plano() => Plataforma.Dominio.Assinaturas.Plano.Criar("Começo", 1, 3, 49.90m, 38.90m);

    private static Assinatura EmTeste() => ServicoAssinatura.IniciarTeste(Guid.NewGuid(), Plano(), Periodicidade.Mensal, "teste", Agora);

    private static (Assinatura Assinatura, CobrancaAssinatura Cobranca) Paga()
    {
        var assinatura = EmTeste();
        var cobranca = ServicoAssinatura.RegistrarPagamento(
            assinatura, 49.90m, FormaCobranca.Pix, Agora, Agora, Agora.AddMonths(1), OrigemCobranca.Gateway, "pay_1", "gateway", Agora);
        return (assinatura, cobranca);
    }

    [Theory]
    [InlineData("529.982.247-25", "***.982.247-**")]
    [InlineData("11.222.333/0001-81", "**.222.333/0001-**")]
    public void Documento_aceita_cpf_ou_cnpj_validos_e_so_expoe_o_mascarado(string valor, string mascarado)
    {
        var documento = DocumentoTitular.Criar(valor);

        documento.Mascarado().Should().Be(mascarado);
        documento.Digitos.Should().MatchRegex("^[0-9]+$");
    }

    [Theory]
    [InlineData("529.982.247-24")]
    [InlineData("11.222.333/0001-80")]
    [InlineData("111.111.111-11")]
    [InlineData("123")]
    [InlineData(null)]
    public void Documento_invalido_e_recusado(string? valor) =>
        FluentActions.Invoking(() => DocumentoTitular.Criar(valor)).Should().Throw<ArgumentException>();

    [Fact]
    public void Cancelar_no_teste_usa_ate_o_fim_e_entao_vira_cancelada_em_vez_de_carencia()
    {
        var assinatura = EmTeste();

        ServicoAssinatura.PedirCancelamento(assinatura, "painel", Agora).Should().BeFalse();
        (assinatura.Estado, assinatura.CancelamentoAgendado).Should().Be((EstadoAssinatura.EmTeste, true));
        FluentActions.Invoking(() => ServicoAssinatura.PedirCancelamento(assinatura, "painel", Agora)).Should().Throw<RegraAssinaturaException>();

        ServicoAssinatura.AtualizarPorTempo(assinatura, assinatura.FimTeste!.Value.AddMinutes(1)).Should().BeTrue();
        assinatura.Estado.Should().Be(EstadoAssinatura.Cancelada);
        assinatura.CarenciaAte.Should().BeNull();
    }

    [Fact]
    public void Cancelar_sem_periodo_pela_frente_cancela_na_hora()
    {
        var assinatura = EmTeste();
        ServicoAssinatura.AtualizarPorTempo(assinatura, assinatura.FimTeste!.Value.AddDays(1)); // atrasada

        ServicoAssinatura.PedirCancelamento(assinatura, "painel", Agora.AddDays(31)).Should().BeTrue();
        assinatura.Estado.Should().Be(EstadoAssinatura.Cancelada);
    }

    [Fact]
    public void Contratar_de_novo_desfaz_o_cancelamento_pedido()
    {
        var (assinatura, _) = Paga();
        ServicoAssinatura.PedirCancelamento(assinatura, "painel", Agora);

        ServicoAssinatura.RegistrarContratacao(assinatura, "asaas", "sub_2", "painel", Agora);

        (assinatura.CancelamentoAgendado, assinatura.IdExternoGateway).Should().Be((false, "sub_2"));
    }

    [Fact]
    public void Primeira_cobranca_e_no_fim_do_teste_ou_hoje_se_ja_passou()
    {
        var assinatura = EmTeste();

        ServicoAssinatura.PrimeiroVencimento(assinatura, Agora).Should().Be(assinatura.FimTeste);
        ServicoAssinatura.PrimeiroVencimento(assinatura, Agora.AddDays(40)).Should().Be(Agora.AddDays(40));
    }

    [Fact]
    public void Cobranca_vencida_entra_em_carencia_contada_do_vencimento()
    {
        var (assinatura, _) = Paga();
        var vencimento = Agora.AddMonths(1);

        ServicoAssinatura.MarcarCobrancaVencida(assinatura, vencimento, "gateway", vencimento.AddHours(1)).Should().BeTrue();

        (assinatura.Estado, assinatura.CarenciaAte).Should().Be((EstadoAssinatura.Atrasada, vencimento.AddDays(Assinatura.DiasCarencia)));
        ServicoAssinatura.MarcarCobrancaVencida(assinatura, vencimento, "gateway", vencimento.AddHours(2)).Should().BeFalse();
    }

    [Fact]
    public void Estorno_tira_o_periodo_e_volta_para_atrasada_com_carencia_a_partir_de_agora()
    {
        var (assinatura, cobranca) = Paga();
        var quando = Agora.AddDays(3);

        ServicoAssinatura.EstornarPagamento(assinatura, cobranca, "gateway", quando);

        cobranca.EstornadaEm.Should().Be(quando);
        (assinatura.Estado, assinatura.ProximoVencimento, assinatura.CarenciaAte)
            .Should().Be((EstadoAssinatura.Atrasada, cobranca.PeriodoInicio, quando.AddDays(Assinatura.DiasCarencia)));

        // Estornar de novo não faz nada.
        var historico = assinatura.Historico.Count;
        ServicoAssinatura.EstornarPagamento(assinatura, cobranca, "gateway", quando.AddDays(1));
        assinatura.Historico.Count.Should().Be(historico);
    }
}
