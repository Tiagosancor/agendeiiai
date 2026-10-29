using System.Text.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>
/// Fase 4, Fatia 8 (remarcar pelo agente): classificador estrito do "sim" à remarcação, textos do servidor, pendência com o retrato do
/// resumo, prompt v6, guardas do sanitizador e ferramentas só no modo Completo.
/// </summary>
public sealed class RemarcarPeloAgenteUnidadeTestes
{
    private static readonly Guid NegocioId = Guid.NewGuid();
    private static readonly DateTimeOffset Agora = new(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(-3));

    // ---------------------------------------------------------------- classificador estrito

    [Theory]
    [InlineData("sim")]
    [InlineData("SIM")]
    [InlineData("Sim!")]
    [InlineData("s")]
    [InlineData("confirmo")]
    [InlineData("Sim, confirmo")]
    [InlineData("pode remarcar")]
    [InlineData("Sim pode remarcar!")]
    [InlineData("sim remarca")]
    public void Afirmativas_da_lista_estrita_confirmam_a_remarcacao(string texto) =>
        ClassificadorConfirmacaoAgente.ClassificarRemarcacao(texto).Should().Be(RespostaDoCliente.Afirmativa);

    [Theory]
    [InlineData("ok")]
    [InlineData("beleza")]
    [InlineData("pode")]
    [InlineData("isso")]
    [InlineData("👍")]
    [InlineData("certo")]
    [InlineData("remarca")]
    [InlineData("remarcar")]
    [InlineData("pode cancelar")]
    [InlineData("sim cancela")]
    [InlineData("sim, mas as 15h")]
    [InlineData("")]
    public void Fora_da_lista_estrita_nao_confirma_nem_recusa(string texto) =>
        ClassificadorConfirmacaoAgente.ClassificarRemarcacao(texto).Should().Be(RespostaDoCliente.Outra);

    [Theory]
    [InlineData("não")]
    [InlineData("nao")]
    [InlineData("n")]
    [InlineData("desisto")]
    [InlineData("mantém")]
    [InlineData("deixa assim")]
    [InlineData("Não quero")]
    [InlineData("não remarca")]
    public void Negativas_recusam_a_remarcacao(string texto) =>
        ClassificadorConfirmacaoAgente.ClassificarRemarcacao(texto).Should().Be(RespostaDoCliente.Negativa);

    [Fact]
    public void No_lote_todas_as_mensagens_precisam_ser_do_mesmo_lado_e_o_texto_longo_nunca_conta()
    {
        ClassificadorConfirmacaoAgente.ClassificarLoteRemarcacao(["sim", "sim confirmo"]).Should().Be(RespostaDoCliente.Afirmativa);
        ClassificadorConfirmacaoAgente.ClassificarLoteRemarcacao(["sim", "melhor as 15h"]).Should().Be(RespostaDoCliente.Outra);
        ClassificadorConfirmacaoAgente.ClassificarLoteRemarcacao(["sim", "não"]).Should().Be(RespostaDoCliente.Outra);
        ClassificadorConfirmacaoAgente.ClassificarLoteRemarcacao(["não", "deixa"]).Should().Be(RespostaDoCliente.Negativa);
        ClassificadorConfirmacaoAgente.ClassificarLoteRemarcacao([]).Should().Be(RespostaDoCliente.Outra);
        ClassificadorConfirmacaoAgente.ClassificarRemarcacao("sim " + new string('a', 40)).Should().Be(RespostaDoCliente.Outra);
    }

    [Fact]
    public void As_listas_de_cancelar_e_de_remarcar_nao_se_misturam()
    {
        // Um "pode cancelar" não confirma uma remarcação e um "pode remarcar" não confirma um cancelamento.
        ClassificadorConfirmacaoAgente.ClassificarCancelamento("pode remarcar").Should().Be(RespostaDoCliente.Outra);
        ClassificadorConfirmacaoAgente.ClassificarRemarcacao("pode cancelar").Should().Be(RespostaDoCliente.Outra);
        ClassificadorConfirmacaoAgente.ClassificarCancelamento("sim").Should().Be(RespostaDoCliente.Afirmativa);
        ClassificadorConfirmacaoAgente.ClassificarRemarcacao("sim").Should().Be(RespostaDoCliente.Afirmativa);
    }

    // ---------------------------------------------------------------- pendência de remarcação

    [Fact]
    public void A_pendencia_de_remarcacao_exige_o_novo_horario_e_o_retrato_do_resumo()
    {
        var profissional = Guid.NewGuid();
        var p = new PendenciaGestaoWhatsApp(
            NegocioId, Guid.NewGuid(), Guid.NewGuid(), TipoGestaoWhatsApp.Remarcar, Agora, Agora.AddMinutes(10),
            Agora.AddDays(1), profissional, Agora.AddDays(2), 45);

        p.NovoInicio.Should().Be(Agora.AddDays(1));
        p.ProfissionalId.Should().Be(profissional);
        p.InicioAnterior.Should().Be(Agora.AddDays(2));
        p.DuracaoMinutos.Should().Be(45);

        FluentActions.Invoking(() => new PendenciaGestaoWhatsApp(
                NegocioId, Guid.NewGuid(), Guid.NewGuid(), TipoGestaoWhatsApp.Remarcar, Agora, Agora.AddMinutes(10), Agora.AddDays(1)))
            .Should().Throw<ArgumentException>("sem o retrato do resumo a remarcação não pode ser conferida no 'sim'");
        FluentActions.Invoking(() => new PendenciaGestaoWhatsApp(
                NegocioId, Guid.NewGuid(), Guid.NewGuid(), TipoGestaoWhatsApp.Remarcar, Agora, Agora.AddMinutes(10), null, profissional, Agora, 30))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PendenciaGestaoWhatsApp(
                NegocioId, Guid.NewGuid(), Guid.NewGuid(), TipoGestaoWhatsApp.Remarcar, Agora, Agora.AddMinutes(10), Agora.AddDays(1), profissional, Agora, 0))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_pendencia_de_cancelamento_ignora_os_campos_de_remarcacao()
    {
        var p = new PendenciaGestaoWhatsApp(
            NegocioId, Guid.NewGuid(), Guid.NewGuid(), TipoGestaoWhatsApp.Cancelar, Agora, Agora.AddMinutes(10),
            Agora.AddDays(1), Guid.NewGuid(), Agora, 30);

        p.NovoInicio.Should().BeNull();
        p.ProfissionalId.Should().BeNull();
        p.InicioAnterior.Should().BeNull();
        p.DuracaoMinutos.Should().BeNull();
    }

    // ---------------------------------------------------------------- textos do servidor

    private static TextosAgendamentoAgente Textos() => new("https://loja.exemplo.com.br", "5571999990000");

    private static DadosDoAgendamentoParaTexto Dados() =>
        new("Barbearia", "Corte", 0m, 30, "Ana", new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(-3)), "America/Sao_Paulo");

    [Fact]
    public void O_resumo_mostra_de_para_e_avisa_que_o_horario_novo_so_fica_garantido_no_sim()
    {
        var novo = new DateTimeOffset(2026, 11, 3, 14, 30, 0, TimeSpan.FromHours(-3));

        var resumo = Textos().ResumoRemarcacao(Dados(), novo, 10);

        resumo.Should().Contain("Confira a remarcação em Barbearia").And.Contain("Corte").And.Contain("Ana")
            .And.Contain("De: segunda-feira, 02/11/2026 às 10:00").And.Contain("Para: terça-feira, 03/11/2026 às 14:30")
            .And.Contain("responda SIM").And.Contain("NÃO").And.Contain("10 minutos").And.Contain("só fica garantido depois do SIM");
    }

    [Fact]
    public void O_texto_de_remarcacao_efetivada_usa_o_horario_novo_e_nao_leva_link_de_gestao()
    {
        var dados = Dados() with { Inicio = new DateTimeOffset(2026, 11, 3, 14, 30, 0, TimeSpan.FromHours(-3)) };

        var texto = Textos().RemarcacaoEfetivada(dados);

        texto.Should().Contain("remarquei").And.Contain("14:30").And.Contain("03/11/2026").And.NotContain("/agendamentos/").And.NotContain("http");
    }

    [Fact]
    public void Os_textos_de_recusa_sao_neutros_e_so_a_antecedencia_e_a_alteracao_sao_explicadas()
    {
        var t = Textos();

        t.RemarcacaoRecusada(MotivoRecusaGestaoAgente.AntecedenciaMinima).Should().Contain("prazo mínimo");
        t.RemarcacaoRecusada(MotivoRecusaGestaoAgente.AgendamentoAlterado).Should().Contain("alterado depois do resumo");
        foreach (var motivo in new[] { MotivoRecusaGestaoAgente.NaoPermitido, MotivoRecusaGestaoAgente.StatusNaoPermite, MotivoRecusaGestaoAgente.Encerrada })
            t.RemarcacaoRecusada(motivo).Should().Be("Não consegui remarcar esse agendamento por aqui. Fale com o estabelecimento.");
    }

    [Fact]
    public void O_texto_de_horario_indisponivel_traz_ate_5_alternativas_e_diz_que_nada_mudou()
    {
        var livres = Enumerable.Range(0, 8).Select(i => new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(-3)).AddMinutes(15 * i)).ToList();

        var comLista = Textos().RemarcacaoHorarioIndisponivel(livres, "America/Sao_Paulo");
        var semLista = Textos().RemarcacaoHorarioIndisponivel(null, "America/Sao_Paulo");

        comLista.Should().Contain("não está mais disponível").And.Contain("continua como estava").And.Contain("03/11/2026 às 09:00").And.Contain("03/11/2026 às 10:00");
        comLista.Should().NotContain("10:15", "só as 5 primeiras alternativas");
        semLista.Should().Contain("continua como estava").And.Contain("outro dia ou horário").And.NotContain("Horários livres");
    }

    // ---------------------------------------------------------------- opções, prompt e sanitizador

    [Fact]
    public void Completo_e_o_unico_modo_com_as_ferramentas_de_remarcar()
    {
        new OpcoesGestaoAgente { Modo = ModoGestaoAgente.Completo }.ModoEfetivo.Should().Be(ModoGestaoAgente.Completo);
        new OpcoesGestaoAgente { Modo = ModoGestaoAgente.Cancelar }.ModoEfetivo.Should().Be(ModoGestaoAgente.Cancelar);
        new OpcoesGestaoAgente { Modo = (ModoGestaoAgente)99 }.ModoEfetivo.Should().Be(ModoGestaoAgente.Desligado, "valor desconhecido nunca liga nada");
        (ModoGestaoAgente.Completo > ModoGestaoAgente.Cancelar).Should().BeTrue();
    }

    [Fact]
    public void Prompt_v6_so_com_o_modo_Completo_e_compoe_com_o_atendente_humano()
    {
        var v5 = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", false, true, true);
        var v5Explicito = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", false, true, true, false);
        var v6 = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", false, true, true, true);
        var v6Humano = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", true, true, true, true);
        var v6SoRemarcar = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", false, false, false, true);

        v5.Should().Be(v5Explicito).And.Contain("prompt v5").And.NotContain("preparar_remarcacao");
        v6.Should().Contain("prompt v6").And.Contain("preparar_remarcacao").And.Contain("consultar_horarios_remarcacao").And.Contain("preparar_cancelamento")
            .And.Contain("responder SIM").And.Contain("Nunca diga que cancelou ou remarcou").And.NotContain("chamar_atendente");
        v6Humano.Should().Contain("prompt v6").And.Contain("chamar_atendente");
        v6SoRemarcar.Should().Be(v6, "remarcar implica cancelar e a gestão por link");
        ConstrutorPromptAgente.VersaoGestaoCompleto.Should().Be("v6");
    }

    [Fact]
    public void A_guarda_do_modo_Completo_tem_aviso_proprio_e_o_do_modo_Cancelar_continua()
    {
        SanitizadorRespostaAgente.AvisoRemarcacaoNaoEfetivadaCompleto.Should().Contain("SIM").And.Contain("ainda não remarquei").And.NotContain("link");
        SanitizadorRespostaAgente.AvisoRemarcacaoNaoEfetivada.Should().Contain("link de gestão");
        SanitizadorRespostaAgente.AfirmaRemarcacao("Pronto, remarquei para as 14h").Should().BeTrue();
        SanitizadorRespostaAgente.AfirmaRemarcacao("Para remarcar, responda SIM").Should().BeFalse();
    }

    // ---------------------------------------------------------------- ferramentas

    [Theory]
    [InlineData("""{"data":"2026-11-03","hora":"14:30"}""", true)]
    [InlineData("""{"agendamento_id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","data":"2026-11-03","hora":"14:30"}""", true)]
    [InlineData("""{"data":"2026-11-03","hora":"14:30","telefone":"5571988887777"}""", false)]
    [InlineData("""{"data":"2026-11-03","hora":"14:30","profissional_id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""", false)]
    [InlineData("""{"data":"2026-11-03","hora":"14:30","negocio_id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""", false)]
    [InlineData("""{"data":"2026-11-03","hora":"14:30","forcar":true}""", false)]
    [InlineData("""{"data":"2026-11-03","hora":"14:30","motivo":"x"}""", false)]
    [InlineData("""{"data":"2026-11-03","hora":1430}""", false)]
    [InlineData("""{"agendamento_id":"nao-e-uuid","data":"2026-11-03","hora":"14:30"}""", false)]
    [InlineData("""[]""", false)]
    public void Leitor_de_argumentos_do_preparar_remarcacao_e_estrito(string json, bool valido)
    {
        var leitor = new LeitorArgumentos(JsonDocument.Parse(json).RootElement.Clone(), "agendamento_id", "data", "hora");
        _ = leitor.Identificador("agendamento_id");
        _ = leitor.Data("data");
        _ = leitor.Texto("hora");

        leitor.Valido.Should().Be(valido);
    }

    [Fact]
    public void Os_esquemas_das_ferramentas_de_remarcar_exigem_data_e_nao_tem_campos_de_identidade()
    {
        var nomes = new[] { "agendamento_id", "data", "hora" };

        foreach (var esquema in new[] { FerramentaPrepararRemarcacaoEsquema(), FerramentaConsultarEsquema() })
        {
            var propriedades = esquema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
            propriedades.Should().BeSubsetOf(nomes);
            propriedades.Should().NotContain(["telefone", "cliente_id", "negocio_id", "profissional_id", "token", "forcar", "motivo"]);
            esquema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
            esquema.GetProperty("required").EnumerateArray().Select(r => r.GetString()).Should().Contain("data");
        }
    }

    private static JsonElement FerramentaPrepararRemarcacaoEsquema() => new FerramentaPrepararRemarcacao(null!, null!, null!, null!, null!, null!, null!, null!, null!).Esquema;

    private static JsonElement FerramentaConsultarEsquema() => new FerramentaConsultarHorariosRemarcacao(null!, null!, null!, null!, null!, null!, null!).Esquema;

    [Fact]
    public void Os_codigos_de_erro_novos_tem_mensagem_fixa_e_nao_vazam_detalhe()
    {
        ErrosFerramenta.Mensagem(ErrosFerramenta.RemarcacaoNaoPermitida).Should().Contain("não pode ser remarcado");
        ErrosFerramenta.Mensagem(ErrosFerramenta.MesmoHorario).Should().Contain("já é o horário");
        ErrosFerramenta.RemarcacaoNaoPermitida.Should().Be("remarcacao_nao_permitida");
        ErrosFerramenta.MesmoHorario.Should().Be("mesmo_horario");
    }

    [Fact]
    public void A_escolha_necessaria_da_remarcacao_tambem_guarda_os_ids_na_nota()
    {
        var id = Guid.NewGuid();
        var resultado = JsonDocument.Parse($$"""
            {"erro":"escolha_necessaria","mensagem":"x","agendamentos":[{"id":"{{id}}","data":"2026-11-03","hora":"14:30"}]}
            """).RootElement.Clone();

        foreach (var ferramenta in new[] { "preparar_remarcacao", "consultar_horarios_remarcacao" })
            NotaContextoAgente.Montar([new ConsultaDaRodada(ferramenta, JsonDocument.Parse("{}").RootElement.Clone(), resultado)])
                .Should().Contain(id.ToString());
    }
}
