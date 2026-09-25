using FluentAssertions;
using Plataforma.Dominio.Assinaturas;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class ServicoAssinaturaTestes
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Plano Comeco = Plano.Criar("Começo", 1, 3, 49.90m, 38.90m, 1);
    private static readonly Plano Ritmo = Plano.Criar("Ritmo", 4, 7, 79.90m, 68.90m, 2, destaque: true);

    private static Assinatura NovaEmTeste(Periodicidade periodicidade = Periodicidade.Mensal) =>
        ServicoAssinatura.IniciarTeste(Guid.NewGuid(), Comeco, periodicidade, "cadastro", Inicio);

    private static CobrancaAssinatura Pagar(Assinatura assinatura, DateTimeOffset agora, int meses = 1) =>
        ServicoAssinatura.RegistrarPagamento(
            assinatura, assinatura.ValorDoPeriodo, FormaCobranca.Pix, agora, agora, agora.AddMonths(meses),
            OrigemCobranca.Manual, null, "admin", agora);

    [Fact]
    public void Iniciar_teste_da_30_dias_trava_o_preco_e_registra_historico()
    {
        var assinatura = NovaEmTeste(Periodicidade.Anual);

        assinatura.Estado.Should().Be(EstadoAssinatura.EmTeste);
        assinatura.FimTeste.Should().Be(Inicio.AddDays(30));
        assinatura.PrecoMensalTravado.Should().Be(38.90m);
        assinatura.ValorDoPeriodo.Should().Be(466.80m);
        assinatura.Historico.Should().ContainSingle(h => h.EstadoAnterior == null && h.EstadoNovo == EstadoAssinatura.EmTeste);
    }

    [Fact]
    public void Plano_inativo_nao_inicia_teste()
    {
        var plano = Plano.Criar("Antigo", 1, 3, 10m, 9m);
        plano.Desativar();

        var acao = () => ServicoAssinatura.IniciarTeste(Guid.NewGuid(), plano, Periodicidade.Mensal, "cadastro", Inicio);

        acao.Should().Throw<RegraAssinaturaException>();
    }

    [Fact]
    public void Teste_ainda_valido_nao_muda()
    {
        var assinatura = NovaEmTeste();

        ServicoAssinatura.AtualizarPorTempo(assinatura, Inicio.AddDays(29)).Should().BeFalse();
        assinatura.Estado.Should().Be(EstadoAssinatura.EmTeste);
    }

    [Fact]
    public void Fim_do_teste_entra_em_carencia_de_5_dias_e_depois_suspende()
    {
        var assinatura = NovaEmTeste();

        ServicoAssinatura.AtualizarPorTempo(assinatura, Inicio.AddDays(30)).Should().BeTrue();
        assinatura.Estado.Should().Be(EstadoAssinatura.Atrasada);
        assinatura.CarenciaAte.Should().Be(Inicio.AddDays(35));
        assinatura.PermiteOperar.Should().BeTrue();

        ServicoAssinatura.AtualizarPorTempo(assinatura, Inicio.AddDays(34)).Should().BeFalse();

        ServicoAssinatura.AtualizarPorTempo(assinatura, Inicio.AddDays(35)).Should().BeTrue();
        assinatura.Estado.Should().Be(EstadoAssinatura.Suspensa);
        assinatura.PermiteOperar.Should().BeFalse();
    }

    [Fact]
    public void Job_parado_por_muitos_dias_encadeia_ate_suspensa_com_os_dois_passos_no_historico()
    {
        var assinatura = NovaEmTeste();

        ServicoAssinatura.AtualizarPorTempo(assinatura, Inicio.AddDays(60));

        assinatura.Estado.Should().Be(EstadoAssinatura.Suspensa);
        assinatura.Historico.Select(h => h.EstadoNovo).Should().Equal(
            EstadoAssinatura.EmTeste, EstadoAssinatura.Atrasada, EstadoAssinatura.Suspensa);
    }

    [Fact]
    public void Pagamento_de_suspensa_volta_a_ativa_na_hora_com_vencimento()
    {
        var assinatura = NovaEmTeste();
        ServicoAssinatura.AtualizarPorTempo(assinatura, Inicio.AddDays(40));

        var agora = Inicio.AddDays(41);
        var cobranca = Pagar(assinatura, agora);

        assinatura.Estado.Should().Be(EstadoAssinatura.Ativa);
        assinatura.ProximoVencimento.Should().Be(agora.AddMonths(1));
        assinatura.CarenciaAte.Should().BeNull();
        cobranca.Valor.Should().Be(49.90m);
        cobranca.Origem.Should().Be(OrigemCobranca.Manual);
    }

    [Fact]
    public void Ativa_vencida_entra_em_carencia()
    {
        var assinatura = NovaEmTeste();
        Pagar(assinatura, Inicio.AddDays(10));

        ServicoAssinatura.AtualizarPorTempo(assinatura, Inicio.AddDays(10).AddMonths(1));

        assinatura.Estado.Should().Be(EstadoAssinatura.Atrasada);
    }

    [Fact]
    public void Pagamento_com_valor_ou_periodo_invalido_e_recusado()
    {
        var assinatura = NovaEmTeste();

        var semValor = () => ServicoAssinatura.RegistrarPagamento(
            assinatura, 0, FormaCobranca.Pix, Inicio, Inicio, Inicio.AddMonths(1), OrigemCobranca.Manual, null, "admin", Inicio);
        var periodoInvertido = () => ServicoAssinatura.RegistrarPagamento(
            assinatura, 10, FormaCobranca.Pix, Inicio, Inicio, Inicio.AddDays(-1), OrigemCobranca.Manual, null, "admin", Inicio);

        semValor.Should().Throw<RegraAssinaturaException>();
        periodoInvertido.Should().Throw<RegraAssinaturaException>();
    }

    [Fact]
    public void Estender_teste_de_suspensa_volta_a_em_teste_a_partir_de_agora()
    {
        var assinatura = NovaEmTeste();
        ServicoAssinatura.AtualizarPorTempo(assinatura, Inicio.AddDays(40));

        var agora = Inicio.AddDays(40);
        ServicoAssinatura.EstenderTeste(assinatura, 15, "admin", agora);

        assinatura.Estado.Should().Be(EstadoAssinatura.EmTeste);
        assinatura.FimTeste.Should().Be(agora.AddDays(15));
    }

    [Fact]
    public void Estender_teste_de_quem_ja_pagou_e_recusado()
    {
        var assinatura = NovaEmTeste();
        Pagar(assinatura, Inicio.AddDays(5));

        var acao = () => ServicoAssinatura.EstenderTeste(assinatura, 10, "admin", Inicio.AddDays(6));

        acao.Should().Throw<RegraAssinaturaException>();
    }

    [Fact]
    public void Trocar_para_plano_menor_com_profissionais_demais_e_recusado_dizendo_quantos_desativar()
    {
        var assinatura = ServicoAssinatura.IniciarTeste(Guid.NewGuid(), Ritmo, Periodicidade.Mensal, "cadastro", Inicio);

        var acao = () => ServicoAssinatura.TrocarPlano(assinatura, Comeco, Periodicidade.Mensal, 5, "admin", Inicio);

        acao.Should().Throw<LimiteProfissionaisExcedidoException>().Which.Excedente.Should().Be(2);
        assinatura.PlanoId.Should().Be(Ritmo.Id);
    }

    [Fact]
    public void Trocar_plano_trava_o_preco_do_novo_plano()
    {
        var assinatura = NovaEmTeste();

        ServicoAssinatura.TrocarPlano(assinatura, Ritmo, Periodicidade.Anual, 4, "admin", Inicio);

        assinatura.PlanoId.Should().Be(Ritmo.Id);
        assinatura.PrecoMensalTravado.Should().Be(68.90m);
        assinatura.Estado.Should().Be(EstadoAssinatura.EmTeste);
    }

    [Fact]
    public void Reativar_volta_ao_estado_anterior_a_suspensao_manual()
    {
        var assinatura = NovaEmTeste();
        Pagar(assinatura, Inicio);
        ServicoAssinatura.Suspender(assinatura, "Pedido do dono", "admin", Inicio.AddDays(1));

        ServicoAssinatura.Reativar(assinatura, "admin", Inicio.AddDays(2));

        assinatura.Estado.Should().Be(EstadoAssinatura.Ativa);
    }

    [Fact]
    public void Reativar_suspensa_por_carencia_ganha_carencia_nova()
    {
        var assinatura = NovaEmTeste();
        ServicoAssinatura.AtualizarPorTempo(assinatura, Inicio.AddDays(40));

        var agora = Inicio.AddDays(41);
        ServicoAssinatura.Reativar(assinatura, "admin", agora);

        assinatura.Estado.Should().Be(EstadoAssinatura.Atrasada);
        assinatura.CarenciaAte.Should().Be(agora.AddDays(Assinatura.DiasCarencia));
        ServicoAssinatura.AtualizarPorTempo(assinatura, agora.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Cancelada_nao_recebe_pagamento()
    {
        var assinatura = NovaEmTeste();
        ServicoAssinatura.Cancelar(assinatura, "Encerrou o negócio", "admin", Inicio);

        var acao = () => Pagar(assinatura, Inicio.AddDays(1));

        acao.Should().Throw<RegraAssinaturaException>();
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(6.5, 7)]
    [InlineData(2.5, 3)]
    [InlineData(0.5, 1)]
    public void Aviso_pendente_escolhe_o_mais_urgente(double diasAntesDoFim, int? esperado)
    {
        var assinatura = NovaEmTeste();

        assinatura.AvisoPendente(Inicio.AddDays(30 - diasAntesDoFim)).Should().Be(esperado);
    }

    [Fact]
    public void Aviso_ja_enviado_nao_repete_e_prazo_novo_zera()
    {
        var assinatura = NovaEmTeste();
        var agora = Inicio.AddDays(24);

        assinatura.MarcarAvisoEnviado(7);
        assinatura.AvisoPendente(agora).Should().BeNull();
        assinatura.AvisoPendente(Inicio.AddDays(28)).Should().Be(3);

        ServicoAssinatura.EstenderTeste(assinatura, 10, "admin", agora);
        assinatura.UltimoAvisoDias.Should().BeNull();
    }
}
