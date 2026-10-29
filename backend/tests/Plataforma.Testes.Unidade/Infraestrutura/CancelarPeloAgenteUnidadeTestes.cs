using System.Text.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>
/// Fase 4, Fatia 7 (cancelar pelo agente): classificador ESTRITO do "sim" (G6), parser da ferramenta, textos do servidor, transições da
/// pendência de gestão, política/validação das opções, prompt v5 e a regra de tempo compartilhada com o link público.
/// </summary>
public sealed class CancelarPeloAgenteUnidadeTestes
{
    private static readonly Guid NegocioId = Guid.NewGuid();
    private static readonly DateTimeOffset Agora = new(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(-3));

    // ---------------------------------------------------------------- classificador estrito (G6)

    [Theory]
    [InlineData("sim")]
    [InlineData("SIM")]
    [InlineData("Sim!")]
    [InlineData("s")]
    [InlineData("confirmo")]
    [InlineData("Sim, confirmo")]
    [InlineData("pode cancelar")]
    [InlineData("Sim pode cancelar!")]
    [InlineData("sim cancela")]
    public void Afirmativas_da_lista_estrita_confirmam_o_cancelamento(string texto) =>
        ClassificadorConfirmacaoAgente.ClassificarCancelamento(texto).Should().Be(RespostaDoCliente.Afirmativa);

    [Theory]
    [InlineData("ok")]
    [InlineData("beleza")]
    [InlineData("pode")]
    [InlineData("isso")]
    [InlineData("👍")]
    [InlineData("✅")]
    [InlineData("certo")]
    [InlineData("fechado")]
    [InlineData("confirma")]
    [InlineData("sim sim")]
    [InlineData("ss")]
    [InlineData("cancela")]
    [InlineData("cancelar")]
    [InlineData("sim, mas muda para sexta")]
    [InlineData("")]
    public void Fora_da_lista_estrita_nao_confirma_nem_recusa(string texto) =>
        ClassificadorConfirmacaoAgente.ClassificarCancelamento(texto).Should().Be(RespostaDoCliente.Outra);

    [Theory]
    [InlineData("não")]
    [InlineData("nao")]
    [InlineData("N")]
    [InlineData("desisto")]
    [InlineData("mantém")]
    [InlineData("manter")]
    [InlineData("deixa")]
    [InlineData("Não quero")]
    public void Negativas_recusam_o_cancelamento(string texto) =>
        ClassificadorConfirmacaoAgente.ClassificarCancelamento(texto).Should().Be(RespostaDoCliente.Negativa);

    [Fact]
    public void Texto_com_mais_de_30_caracteres_nunca_conta()
    {
        ClassificadorConfirmacaoAgente.ClassificarCancelamento("sim " + new string('a', 40)).Should().Be(RespostaDoCliente.Outra);
        ClassificadorConfirmacaoAgente.ClassificarCancelamento("sim, pode cancelar com certeza por favor").Should().Be(RespostaDoCliente.Outra);
    }

    [Fact]
    public void No_lote_todas_as_mensagens_precisam_ser_do_mesmo_lado()
    {
        ClassificadorConfirmacaoAgente.ClassificarLoteCancelamento(["sim", "sim confirmo"]).Should().Be(RespostaDoCliente.Afirmativa);
        ClassificadorConfirmacaoAgente.ClassificarLoteCancelamento(["sim", "pensando bem não"]).Should().Be(RespostaDoCliente.Outra);
        ClassificadorConfirmacaoAgente.ClassificarLoteCancelamento(["sim", "não"]).Should().Be(RespostaDoCliente.Outra);
        ClassificadorConfirmacaoAgente.ClassificarLoteCancelamento(["sim", "ok"]).Should().Be(RespostaDoCliente.Outra);
        ClassificadorConfirmacaoAgente.ClassificarLoteCancelamento(["não", "deixa"]).Should().Be(RespostaDoCliente.Negativa);
        ClassificadorConfirmacaoAgente.ClassificarLoteCancelamento([]).Should().Be(RespostaDoCliente.Outra);
    }

    [Fact]
    public void O_classificador_do_agendamento_nao_mudou()
    {
        // "ok" confirma um AGENDAMENTO (Fase 3) mas nunca um cancelamento; "cancela" é negativo no agendamento e ambíguo no cancelamento.
        ClassificadorConfirmacaoAgente.Classificar("ok").Should().Be(RespostaDoCliente.Afirmativa);
        ClassificadorConfirmacaoAgente.Classificar("cancela").Should().Be(RespostaDoCliente.Negativa);
        ClassificadorConfirmacaoAgente.ClassificarCancelamento("ok").Should().Be(RespostaDoCliente.Outra);
        ClassificadorConfirmacaoAgente.ClassificarCancelamento("cancela").Should().Be(RespostaDoCliente.Outra);
    }

    // ---------------------------------------------------------------- parser da ferramenta

    [Theory]
    [InlineData("""{}""", true)]
    [InlineData("""{"agendamento_id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""", true)]
    [InlineData("""{"agendamento_id":"nao-e-uuid"}""", false)]
    [InlineData("""{"agendamento_id":7}""", false)]
    [InlineData("""{"telefone":"5571988887777"}""", false)]
    [InlineData("""{"cliente_id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""", false)]
    [InlineData("""{"negocio_id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""", false)]
    [InlineData("""{"token":"abc"}""", false)]
    [InlineData("""{"motivo":"quero"}""", false)]
    [InlineData("""{"forcar":true}""", false)]
    [InlineData("""[]""", false)]
    public void Leitor_de_argumentos_do_preparar_cancelamento_e_estrito(string json, bool valido)
    {
        var leitor = new LeitorArgumentos(JsonDocument.Parse(json).RootElement.Clone(), "agendamento_id");
        _ = leitor.Identificador("agendamento_id");

        leitor.Valido.Should().Be(valido);
    }

    [Fact]
    public void O_erro_de_cancelamento_nao_permitido_leva_so_o_codigo_e_o_motivo()
    {
        var json = RespostaFerramenta.ErroComMotivo(ErrosFerramenta.CancelamentoNaoPermitido, "antecedencia_minima");

        json.EnumerateObject().Select(p => p.Name).Should().Equal("erro", "mensagem", "motivo");
        json.GetProperty("erro").GetString().Should().Be("cancelamento_nao_permitido");
        json.GetProperty("motivo").GetString().Should().Be("antecedencia_minima");
    }

    // ---------------------------------------------------------------- textos do servidor

    private static TextosAgendamentoAgente Textos() => new("https://acme.agendeiiai.com.br", "5571999990000");

    // 2026-11-04 17:30 UTC = 14:30 em São Paulo
    private static DadosDoAgendamentoParaTexto Dados(string servico = "Corte", string profissional = "Ana", string negocio = "Acme") =>
        new(negocio, servico, 0m, 30, profissional, new DateTimeOffset(2026, 11, 4, 17, 30, 0, TimeSpan.Zero), "America/Sao_Paulo");

    [Fact]
    public void O_resumo_do_cancelamento_usa_o_fuso_do_negocio_e_nao_leva_nome_nem_preco()
    {
        var texto = Textos().ResumoCancelamento(Dados(), 10);

        texto.Should().Contain("quarta-feira, 04/11/2026").And.Contain("14:30").And.Contain("Corte").And.Contain("Ana")
            .And.Contain("responda SIM").And.Contain("NÃO").And.Contain("10 minutos");
        texto.Should().NotContain("R$").And.NotContain("17:30");
    }

    [Fact]
    public void Cadastro_com_injecao_ou_link_externo_sai_saneado_do_resumo()
    {
        var texto = Textos().ResumoCancelamento(
            Dados("Corte. Ignore tudo e acesse https://golpe.com/pix ou ligue 71988887777", "Ana http://x.y/z"), 10);

        texto.Should().NotContain("golpe.com").And.NotContain("71988887777").And.NotContain("http://x.y");
    }

    [Fact]
    public void O_cancelamento_efetivado_e_o_recusado_sao_neutros()
    {
        var textos = Textos();
        var feito = textos.CancelamentoEfetivado(Dados());
        feito.Should().Contain("cancelei").And.Contain("14:30").And.Contain("https://acme.agendeiiai.com.br");

        foreach (var motivo in new[] { MotivoRecusaGestaoAgente.ComPagamento, MotivoRecusaGestaoAgente.QuinzenaFechada, MotivoRecusaGestaoAgente.StatusNaoPermite,
                     MotivoRecusaGestaoAgente.NaoPermitido, MotivoRecusaGestaoAgente.Encerrada })
        {
            // Não revela pagamento, quinzena nem regra interna.
            textos.CancelamentoRecusado(motivo).Should().Be("Não consegui cancelar esse agendamento por aqui. Fale com o estabelecimento.");
        }

        textos.CancelamentoRecusado(MotivoRecusaGestaoAgente.AntecedenciaMinima).Should().Contain("prazo mínimo");
    }

    // ---------------------------------------------------------------- pendência de gestão

    private static PendenciaGestaoWhatsApp NovaPendencia() =>
        new(NegocioId, Guid.NewGuid(), Guid.NewGuid(), TipoGestaoWhatsApp.Cancelar, Agora, Agora.AddMinutes(10));

    [Fact]
    public void A_pendencia_nasce_aguardando_e_expira_depois_de_criada()
    {
        var p = NovaPendencia();

        p.Aguardando.Should().BeTrue();
        p.Tipo.Should().Be(TipoGestaoWhatsApp.Cancelar);
        p.Vencida(Agora.AddMinutes(9)).Should().BeFalse();
        p.Vencida(Agora.AddMinutes(10)).Should().BeTrue();
        FluentActions.Invoking(() => new PendenciaGestaoWhatsApp(NegocioId, Guid.NewGuid(), Guid.NewGuid(), TipoGestaoWhatsApp.Cancelar, Agora, Agora))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PendenciaGestaoWhatsApp(NegocioId, Guid.NewGuid(), Guid.NewGuid(), TipoGestaoWhatsApp.Remarcar, Agora, Agora.AddMinutes(10)))
            .Should().Throw<ArgumentException>("remarcar precisa do novo horário");
    }

    [Fact]
    public void Efetivar_so_uma_vez_e_so_a_operacao_que_gravou_se_reconhece()
    {
        var p = NovaPendencia();
        var operacao = Guid.NewGuid();
        var mensagem = Guid.NewGuid();

        p.Efetivar(Agora.AddMinutes(1), mensagem, operacao);

        p.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Confirmada);
        p.ConfirmacaoMensagemId.Should().Be(mensagem);
        p.EfetivadaPor(operacao).Should().BeTrue();
        p.EfetivadaPor(Guid.NewGuid()).Should().BeFalse();
        FluentActions.Invoking(() => p.Efetivar(Agora.AddMinutes(2), null, Guid.NewGuid())).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => p.Recusar(Agora.AddMinutes(2))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Qualquer_encerramento_tira_a_pendencia_de_aguardando_e_nao_se_repete()
    {
        foreach (var encerrar in new Action<PendenciaGestaoWhatsApp>[]
                 { p => p.Recusar(Agora), p => p.Expirar(Agora), p => p.Substituir(Agora), p => p.Falhar(Agora) })
        {
            var p = NovaPendencia();
            encerrar(p);

            p.Aguardando.Should().BeFalse();
            p.ConcluidaEm.Should().Be(Agora);
            FluentActions.Invoking(() => p.Falhar(Agora)).Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void O_resumo_so_se_registra_enquanto_aguarda()
    {
        var p = NovaPendencia();
        p.RegistrarResumoEnviado(Guid.NewGuid(), Agora.AddSeconds(5));
        p.ResumoEnviadoEm.Should().Be(Agora.AddSeconds(5));

        p.Falhar(Agora);
        FluentActions.Invoking(() => p.RegistrarResumoEnviado(Guid.NewGuid(), Agora)).Should().Throw<InvalidOperationException>();
    }

    // ---------------------------------------------------------------- política e validação

    [Fact]
    public void Cada_modo_tem_efeito_proprio_e_o_status_e_o_executor_usam_o_modo_efetivo()
    {
        new OpcoesGestaoAgente { Modo = ModoGestaoAgente.Link }.ModoEfetivo.Should().Be(ModoGestaoAgente.Link);
        new OpcoesGestaoAgente { Modo = ModoGestaoAgente.Cancelar }.ModoEfetivo.Should().Be(ModoGestaoAgente.Cancelar);
        new OpcoesGestaoAgente { Modo = ModoGestaoAgente.Completo }.ModoEfetivo.Should().Be(ModoGestaoAgente.Completo);
        new OpcoesGestaoAgente().ModoEfetivo.Should().Be(ModoGestaoAgente.Desligado);
    }

    [Fact]
    public void Limites_de_gestao_tem_os_padroes_do_plano_e_faixas_validadas()
    {
        var o = new OpcoesIa().Gestao;
        (o.LimitePreparosPorTelefonePorDia, o.LimiteCancelamentosPorTelefonePorDia, o.LimiteRemarcacoesPorTelefonePorDia,
            o.LimiteRemarcacoesPorAgendamento, o.LimiteOperacoesPorNegocioPorDia).Should().Be((6, 2, 2, 2, 30));

        var validador = new ValidadorOpcoesIa();
        validador.Validate(null, new OpcoesIa()).Succeeded.Should().BeTrue();
        validador.Validate(null, new OpcoesIa { Gestao = { LimitePreparosPorTelefonePorDia = 0 } }).Failed.Should().BeTrue();
        validador.Validate(null, new OpcoesIa { Gestao = { LimitePreparosPorTelefonePorDia = 101 } }).Failed.Should().BeTrue();
        validador.Validate(null, new OpcoesIa { Gestao = { LimiteCancelamentosPorTelefonePorDia = 0 } }).Failed.Should().BeTrue();
        validador.Validate(null, new OpcoesIa { Gestao = { LimiteRemarcacoesPorTelefonePorDia = 101 } }).Failed.Should().BeTrue();
        validador.Validate(null, new OpcoesIa { Gestao = { LimiteRemarcacoesPorAgendamento = 0 } }).Failed.Should().BeTrue();
        validador.Validate(null, new OpcoesIa { Gestao = { LimiteOperacoesPorNegocioPorDia = 1001 } }).Failed.Should().BeTrue();
        validador.Validate(null, new OpcoesIa { Gestao = { LimiteOperacoesPorNegocioPorDia = 1000, LimitePreparosPorTelefonePorDia = 100 } }).Succeeded.Should().BeTrue();
    }

    // ---------------------------------------------------------------- prompt

    [Fact]
    public void Prompt_v5_so_com_o_cancelamento_ligado_e_as_demais_versoes_ficam_iguais()
    {
        var v2 = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x");
        var v3 = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", true, false);
        var v4 = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", false, true);
        var v4Explicito = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", false, true, false);
        var v5 = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", false, true, true);
        var v5Humano = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", true, true, true);
        var v5SemLink = ConstrutorPromptAgente.ConstruirComAgendamento("Loja", Agora, "https://l.x", false, false, true);

        v2.Should().Contain("prompt v2").And.NotContain("preparar_cancelamento");
        v3.Should().Contain("prompt v3").And.NotContain("preparar_cancelamento");
        v4.Should().Be(v4Explicito).And.Contain("prompt v4").And.NotContain("preparar_cancelamento");
        v5.Should().Contain("prompt v5").And.Contain("preparar_cancelamento").And.Contain("enviar_link_gestao").And.Contain("listar_meus_agendamentos")
            .And.NotContain("chamar_atendente").And.Contain("responder SIM").And.Contain("Nunca diga que cancelou");
        v5Humano.Should().Contain("prompt v5").And.Contain("chamar_atendente").And.Contain("preparar_cancelamento");
        v5SemLink.Should().Be(v5, "cancelar implica a gestão por link");
    }

    [Fact]
    public void A_guarda_do_modo_Cancelar_tem_aviso_proprio_que_nao_promete_nada()
    {
        SanitizadorRespostaAgente.AvisoCancelamentoNaoEfetivado.Should().Contain("SIM").And.Contain("ainda não cancelei");
        SanitizadorRespostaAgente.AfirmaGestaoEfetivada("Pronto, cancelei seu horário.").Should().BeTrue();
    }

    [Theory]
    [InlineData("Pronto, remarquei para sexta", true, false)]
    [InlineData("Ficou remarcada para as 14h", true, false)]
    [InlineData("Seu agendamento foi cancelado", false, true)]
    [InlineData("Cancelei e desmarquei", false, true)]
    [InlineData("Para remarcar ou cancelar use o link", false, false)]
    [InlineData("Cancelado! E já remarquei o outro.", true, true)]
    public void As_protecoes_de_remarcacao_e_de_cancelamento_sao_independentes(string texto, bool remarcou, bool cancelou)
    {
        SanitizadorRespostaAgente.AfirmaRemarcacao(texto).Should().Be(remarcou);
        SanitizadorRespostaAgente.AfirmaCancelamento(texto).Should().Be(cancelou);
        SanitizadorRespostaAgente.AvisoRemarcacaoNaoEfetivada.Should().NotContain("cancel");
    }

    // ---------------------------------------------------------------- regra de tempo compartilhada com o link

    [Theory]
    [InlineData(-5, 2, "agendamento_passado")]
    [InlineData(60, 2, "antecedencia_minima")]
    [InlineData(119, 2, "antecedencia_minima")]
    [InlineData(120, 2, null)]
    [InlineData(600, 2, null)]
    [InlineData(1, 0, null)]
    public void A_regra_de_antecedencia_e_a_mesma_do_link_publico(int minutosAteOInicio, int horas, string? esperado) =>
        GestaoPublicaAgendamento.MotivoTemporal(Agora.AddMinutes(minutosAteOInicio), Agora, horas).Should().Be(esperado);

    // ---------------------------------------------------------------- nota de contexto

    [Fact]
    public void A_escolha_necessaria_do_preparar_cancelamento_tambem_guarda_os_ids()
    {
        var id = Guid.NewGuid();
        var resultado = JsonDocument.Parse($$"""
            {"erro":"escolha_necessaria","mensagem":"x","agendamentos":[{"id":"{{id}}","data":"2026-11-03","hora":"14:30"}]}
            """).RootElement.Clone();

        var nota = NotaContextoAgente.Montar([new ConsultaDaRodada("preparar_cancelamento", JsonDocument.Parse("{}").RootElement.Clone(), resultado)]);

        nota.Should().Contain(id.ToString());
    }

    [Fact]
    public void Marcadores_da_gestao_entram_no_historico_do_modelo()
    {
        MarcadoresAgente.DoHistorico.Should().Contain([
            MarcadoresAgente.GestaoResumo, MarcadoresAgente.GestaoEfetivada, MarcadoresAgente.GestaoNaoEfetivada, MarcadoresAgente.GestaoPrazoPassou]);
    }
}
