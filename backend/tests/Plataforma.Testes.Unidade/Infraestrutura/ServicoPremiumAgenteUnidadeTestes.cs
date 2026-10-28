using FluentAssertions;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>Serviço premium do agente de IA (Fase 4, Fatia 1b): regra pura do gate, domínio, texto fixo e opções.</summary>
public sealed class ServicoPremiumAgenteUnidadeTestes
{
    private static readonly Guid NegocioId = Guid.NewGuid();
    private static readonly Guid Outro = Guid.NewGuid();

    private static OpcoesIa Opcoes(
        ProvedorIa provedor = ProvedorIa.Gemini, string? lista = null, ModoAgendamentoAgente modo = ModoAgendamentoAgente.Desligado, string? listaAgendamento = null) =>
        new() { Provider = provedor, NegociosHabilitados = lista, Agendamento = { Modo = modo, Negocios = listaAgendamento } };

    // ---------------------------------------------------------------- PoliticaAgenteIa.Calcular

    [Fact]
    public void Sem_contratacao_nada_e_liberado_mesmo_com_tudo_ligado_no_global()
    {
        var situacao = PoliticaAgenteIa.Calcular(Opcoes(lista: NegocioId.ToString(), modo: ModoAgendamentoAgente.Todos), NegocioId, contratado: false);

        situacao.Should().Be(new SituacaoAgenteIa(false, false, false));
    }

    [Fact]
    public void Contratado_e_na_lista_global_libera_a_ia_e_o_agendamento_so_com_o_modo()
    {
        PoliticaAgenteIa.Calcular(Opcoes(lista: NegocioId.ToString()), NegocioId, true).Should().Be(new SituacaoAgenteIa(true, true, false));
        PoliticaAgenteIa.Calcular(Opcoes(modo: ModoAgendamentoAgente.Todos), NegocioId, true).Should().Be(new SituacaoAgenteIa(true, true, true));
    }

    [Fact]
    public void Lista_global_so_restringe_contratado_fora_dela_fica_calado()
    {
        PoliticaAgenteIa.Calcular(Opcoes(lista: Outro.ToString(), modo: ModoAgendamentoAgente.Todos), NegocioId, true)
            .Should().Be(new SituacaoAgenteIa(true, false, false));
    }

    [Fact]
    public void Lista_global_vazia_vale_para_todos_os_contratados()
    {
        PoliticaAgenteIa.Calcular(Opcoes(lista: null), NegocioId, true).IaLiberada.Should().BeTrue();
        PoliticaAgenteIa.Calcular(Opcoes(lista: "  "), NegocioId, true).IaLiberada.Should().BeTrue();
    }

    [Fact]
    public void Piloto_de_agendamento_vazio_continua_sem_nenhum_negocio_e_com_a_lista_so_o_dela()
    {
        PoliticaAgenteIa.Calcular(Opcoes(modo: ModoAgendamentoAgente.Piloto, listaAgendamento: null), NegocioId, true).AgendamentoLiberado.Should().BeFalse();
        PoliticaAgenteIa.Calcular(Opcoes(modo: ModoAgendamentoAgente.Piloto, listaAgendamento: NegocioId.ToString()), NegocioId, true).AgendamentoLiberado.Should().BeTrue();
        PoliticaAgenteIa.Calcular(Opcoes(modo: ModoAgendamentoAgente.Piloto, listaAgendamento: Outro.ToString()), NegocioId, true).AgendamentoLiberado.Should().BeFalse();
    }

    [Theory]
    [InlineData(ProvedorIa.Eco)]
    [InlineData(ProvedorIa.Desligado)]
    public void Fora_do_Gemini_a_ia_nunca_e_liberada_para_o_orquestrador(ProvedorIa provedor)
    {
        PoliticaAgenteIa.Calcular(Opcoes(provedor, modo: ModoAgendamentoAgente.Todos), NegocioId, true).Should().Be(new SituacaoAgenteIa(true, false, false));
    }

    // ---------------------------------------------------------------- Negocio.LigarAgenteIa / DesligarAgenteIa

    private static Plataforma.Dominio.Negocios.Negocio NovoNegocio() => Plataforma.Dominio.Negocios.Negocio.Criar(Slug.Criar("premium-teste"), "Premium", TipoNegocio.Barbearia);

    [Fact]
    public void Negocio_novo_nasce_sem_o_servico()
    {
        var negocio = NovoNegocio();

        negocio.AgenteIaContratado.Should().BeFalse();
        negocio.AgenteIaOrigem.Should().BeNull();
        negocio.AgenteIaAlteradoEm.Should().BeNull();
        negocio.AgenteIaAlteradoPor.Should().BeNull();
    }

    [Fact]
    public void Ligar_grava_origem_autor_e_data_e_repetir_nao_muda_nada()
    {
        var negocio = NovoNegocio();
        var t1 = DateTimeOffset.UtcNow;

        negocio.LigarAgenteIa(OrigemAgenteIa.Manual, "plataforma:dono@x.com", t1).Should().BeTrue();
        negocio.LigarAgenteIa(OrigemAgenteIa.Adicional, "outro", t1.AddHours(1)).Should().BeFalse("já estava ligado");

        negocio.AgenteIaContratado.Should().BeTrue();
        negocio.AgenteIaOrigem.Should().Be(OrigemAgenteIa.Manual);
        negocio.AgenteIaAlteradoEm.Should().Be(t1);
        negocio.AgenteIaAlteradoPor.Should().Be("plataforma:dono@x.com");
    }

    [Fact]
    public void Desligar_zera_a_origem_e_repetir_nao_muda_nada()
    {
        var negocio = NovoNegocio();
        negocio.LigarAgenteIa(OrigemAgenteIa.Plano, "a", DateTimeOffset.UtcNow);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(5);

        negocio.DesligarAgenteIa("b", t2).Should().BeTrue();
        negocio.DesligarAgenteIa("c", t2.AddHours(1)).Should().BeFalse("já estava desligado");

        negocio.AgenteIaContratado.Should().BeFalse();
        negocio.AgenteIaOrigem.Should().BeNull();
        negocio.AgenteIaAlteradoEm.Should().Be(t2);
        negocio.AgenteIaAlteradoPor.Should().Be("b");
    }

    [Fact]
    public void Autor_e_obrigatorio_e_origem_invalida_e_recusada()
    {
        var negocio = NovoNegocio();

        FluentActions.Invoking(() => negocio.LigarAgenteIa(OrigemAgenteIa.Manual, " ", DateTimeOffset.UtcNow)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => negocio.LigarAgenteIa((OrigemAgenteIa)99, "a", DateTimeOffset.UtcNow)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => negocio.DesligarAgenteIa("", DateTimeOffset.UtcNow)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Enum_de_origem_tem_os_tres_valores_desde_ja()
    {
        Enum.GetNames<OrigemAgenteIa>().Should().BeEquivalentTo("Manual", "Plano", "Adicional");
    }

    // ---------------------------------------------------------------- texto fixo

    private const string Link = "https://barbearia.agendeiiai.com.br";

    [Fact]
    public void Texto_fixo_traz_o_nome_do_negocio_e_o_link_e_nao_promete_resposta_humana()
    {
        var texto = TextosAgendamentoAgente.AtendimentoIndisponivel("Barbearia do Zé", Link);

        texto.Should().Be("Olá! O atendimento automático de Barbearia do Zé não está disponível no momento. Para ver horários e agendar, acesse: " + Link);
        texto.Should().NotContainEquivalentOf("pagamento").And.NotContainEquivalentOf("contrat");
    }

    [Fact]
    public void Texto_fixo_depois_do_sanitizador_nao_deixa_passar_link_nem_telefone_injetados_no_nome()
    {
        var bruto = TextosAgendamentoAgente.AtendimentoIndisponivel("Barbearia golpe.ai/pix e https://evil.com/x ligue 71 99999-8888", Link);

        var seguro = SanitizadorRespostaAgente.SanitizarTextoFixo(bruto, Link, "5571999990000");

        seguro.Should().NotContain("evil.com").And.NotContain("golpe.ai").And.NotContain("99999");
        seguro.Should().Contain(Link);
    }

    // ---------------------------------------------------------------- opções

    private static readonly ValidadorOpcoesIa Validador = new();

    [Fact]
    public void Padroes_das_opcoes_do_aviso_fixo_sao_24_horas_e_200_por_dia_e_sao_validos()
    {
        var opcoes = new OpcoesIa();

        opcoes.Indisponivel.IntervaloHoras.Should().Be(24);
        opcoes.Indisponivel.LimitePorNegocioPorDia.Should().Be(200);
        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(169, 200)]
    [InlineData(24, 0)]
    [InlineData(24, 10_001)]
    public void Opcoes_do_aviso_fixo_fora_da_faixa_sao_recusadas(int intervalo, int limite)
    {
        var opcoes = new OpcoesIa { Indisponivel = { IntervaloHoras = intervalo, LimitePorNegocioPorDia = limite } };

        Validador.Validate(null, opcoes).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(168, 10_000)]
    public void Opcoes_do_aviso_fixo_nos_limites_sao_aceitas(int intervalo, int limite)
    {
        var opcoes = new OpcoesIa { Indisponivel = { IntervaloHoras = intervalo, LimitePorNegocioPorDia = limite } };

        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
    }
}
