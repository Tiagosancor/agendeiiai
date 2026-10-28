using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Usuarios;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>Atendimento humano (Fase 4, Fatia 3): transições do domínio, textos, prompt, opções e catálogo de ferramentas.</summary>
public sealed class AtendimentoHumanoUnidadeTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static ConversaWhatsApp NovaConversa() => new(Guid.NewGuid(), Guid.NewGuid(), "+5571988887777", Agora.AddDays(-1));

    // ---------------------------------------------------------------- domínio

    [Fact]
    public void Assumir_pelo_painel_muda_para_Humano_com_prazo_autor_e_motivo()
    {
        var conversa = NovaConversa();
        var usuario = Guid.NewGuid();

        conversa.AssumirPeloPainel(usuario, Agora, TimeSpan.FromHours(24)).Should().BeTrue();

        conversa.Status.Should().Be(StatusConversaWhatsApp.Humano);
        conversa.AgenteResponde.Should().BeFalse();
        conversa.HumanoMotivo.Should().Be(MotivoHumanoConversa.Painel);
        conversa.HumanoPorUsuarioId.Should().Be(usuario);
        conversa.HumanoDesde.Should().Be(Agora);
        conversa.HumanoAte.Should().Be(Agora.AddHours(24));
    }

    [Fact]
    public void Assumir_e_idempotente_e_so_registra_quem_atende_se_ninguem_constava()
    {
        var conversa = NovaConversa();
        conversa.ChamarAtendentePeloAgente(Agora, null).Should().BeTrue();
        var usuario = Guid.NewGuid();

        conversa.AssumirPeloPainel(usuario, Agora.AddMinutes(1), null).Should().BeTrue("passa a constar quem atende");
        conversa.HumanoMotivo.Should().Be(MotivoHumanoConversa.PedidoDoAgente, "o motivo original fica");
        conversa.HumanoPorUsuarioId.Should().Be(usuario);
        conversa.HumanoDesde.Should().Be(Agora);

        conversa.AssumirPeloPainel(Guid.NewGuid(), Agora.AddMinutes(2), null).Should().BeFalse("já tem quem atenda");
        conversa.HumanoPorUsuarioId.Should().Be(usuario);
    }

    [Fact]
    public void Sem_prazo_a_conversa_em_Humano_nunca_vence()
    {
        var conversa = NovaConversa();
        conversa.AssumirPeloPainel(Guid.NewGuid(), Agora, null);

        conversa.HumanoAte.Should().BeNull();
        conversa.PrazoHumanoVencido(Agora.AddYears(1)).Should().BeFalse();
        conversa.RenovarPrazoHumano(Agora.AddHours(1), TimeSpan.FromHours(24));
        conversa.HumanoAte.Should().BeNull("sem prazo, nada é renovado");
    }

    [Fact]
    public void Cada_mensagem_adia_o_prazo_e_depois_do_prazo_ele_esta_vencido()
    {
        var conversa = NovaConversa();
        conversa.AssumirPeloPainel(Guid.NewGuid(), Agora, TimeSpan.FromHours(24));

        conversa.PrazoHumanoVencido(Agora.AddHours(23)).Should().BeFalse();
        conversa.RenovarPrazoHumano(Agora.AddHours(23), TimeSpan.FromHours(24));
        conversa.HumanoAte.Should().Be(Agora.AddHours(47));
        conversa.PrazoHumanoVencido(Agora.AddHours(30)).Should().BeFalse();
        conversa.PrazoHumanoVencido(Agora.AddHours(47)).Should().BeTrue();
    }

    [Fact]
    public void O_agente_so_chama_atendente_de_uma_conversa_com_o_agente()
    {
        var conversa = NovaConversa();
        conversa.ChamarAtendentePeloAgente(Agora, TimeSpan.FromHours(1)).Should().BeTrue();
        conversa.HumanoMotivo.Should().Be(MotivoHumanoConversa.PedidoDoAgente);
        conversa.HumanoPorUsuarioId.Should().BeNull();

        conversa.ChamarAtendentePeloAgente(Agora.AddMinutes(5), TimeSpan.FromHours(1)).Should().BeFalse("já está em Humano");
        conversa.HumanoDesde.Should().Be(Agora);
    }

    [Fact]
    public void Devolver_leva_o_processado_ate_a_ultima_mensagem_e_limpa_o_estado_humano()
    {
        var conversa = NovaConversa();
        conversa.AssumirPeloPainel(Guid.NewGuid(), Agora, TimeSpan.FromHours(24));
        var ultima = Agora.AddHours(2);

        conversa.Devolver(Agora.AddHours(3), ultima).Should().BeTrue();

        conversa.Status.Should().Be(StatusConversaWhatsApp.Agente);
        conversa.ProcessadaAte.Should().Be(ultima, "o acumulado nunca é respondido");
        conversa.DevolvidaEm.Should().Be(Agora.AddHours(3));
        conversa.HumanoDesde.Should().BeNull();
        conversa.HumanoAte.Should().BeNull();
        conversa.HumanoMotivo.Should().BeNull();
        conversa.HumanoPorUsuarioId.Should().BeNull();
        conversa.Devolver(Agora.AddHours(4), ultima).Should().BeFalse("já estava com o agente");
        conversa.DevolvidaEm.Should().Be(Agora.AddHours(3));
    }

    [Fact]
    public void Devolver_nunca_recua_o_processado_ate()
    {
        var conversa = NovaConversa();
        conversa.AssumirPeloPainel(Guid.NewGuid(), Agora, null);
        conversa.Devolver(Agora, Agora.AddHours(5));
        conversa.AssumirPeloPainel(Guid.NewGuid(), Agora.AddHours(6), null);

        conversa.Devolver(Agora.AddHours(7), Agora.AddHours(1));

        conversa.ProcessadaAte.Should().Be(Agora.AddHours(5));
    }

    [Fact]
    public void Anonimizar_tira_a_conversa_do_atendimento_humano()
    {
        var conversa = NovaConversa();
        conversa.AssumirPeloPainel(Guid.NewGuid(), Agora, TimeSpan.FromHours(1));
        conversa.RegistrarAtendenteAvisado(Agora);

        conversa.Anonimizar(new HashSet<string>());

        conversa.Status.Should().Be(StatusConversaWhatsApp.Agente);
        conversa.HumanoDesde.Should().BeNull();
        conversa.HumanoPorUsuarioId.Should().BeNull();
        conversa.AtendenteAvisadoEm.Should().BeNull();
        conversa.ChamarAtendentePeloAgente(Agora, null).Should().BeFalse("conversa anonimizada não vai a Humano");
    }

    [Fact]
    public void Mensagem_de_transferencia_da_instancia_valida_tamanho_e_controle()
    {
        var instancia = new InstanciaWhatsApp(Guid.NewGuid(), "neg-x");

        instancia.DefinirMensagemTransferencia("  Atendemos de segunda a sexta.  ");
        instancia.MensagemTransferencia.Should().Be("Atendemos de segunda a sexta.");

        instancia.DefinirMensagemTransferencia(new string('a', InstanciaWhatsApp.MaximoMensagemTransferencia));
        var longa = () => instancia.DefinirMensagemTransferencia(new string('a', InstanciaWhatsApp.MaximoMensagemTransferencia + 1));
        longa.Should().Throw<ArgumentException>();
        var controle = () => instancia.DefinirMensagemTransferencia("oi\u0007");
        controle.Should().Throw<ArgumentException>();

        instancia.DefinirMensagemTransferencia("  ");
        instancia.MensagemTransferencia.Should().BeNull("vazio tira a mensagem");
    }

    // ---------------------------------------------------------------- permissão

    [Fact]
    public void Atender_conversas_e_a_permissao_21_e_so_o_Administrador_a_tem_por_padrao()
    {
        ((int)Permissao.AtenderConversasWhatsApp).Should().Be(21);
        Usuario.PermissoesPadrao(Perfil.Administrador).Should().Contain(Permissao.AtenderConversasWhatsApp);
        Usuario.PermissoesPadrao(Perfil.Recepcionista).Should().NotContain(Permissao.AtenderConversasWhatsApp);
        Usuario.PermissoesPadrao(Perfil.Profissional).Should().NotContain(Permissao.AtenderConversasWhatsApp);
    }

    // ---------------------------------------------------------------- textos

    [Fact]
    public void Texto_de_transferencia_e_fixo_mais_a_mensagem_do_negocio_sem_link_nem_telefone_alheios()
    {
        var texto = TextosHumanoAgente.Transferencia(
            "Barbearia do Zé", "Atendemos até as 18h. Veja https://golpe.com/pix ou ligue 5511999990000.", "https://acme.agendeiiai.com.br", "5571999990000");

        texto.Should().StartWith("Vou chamar alguém da equipe de Barbearia do Zé. Assim que possível, uma pessoa responde por aqui.");
        texto.Should().Contain("Atendemos até as 18h").And.NotContain("golpe.com").And.NotContain("5511999990000");
    }

    [Fact]
    public void Texto_de_transferencia_sem_mensagem_do_negocio_e_so_o_fixo()
    {
        TextosHumanoAgente.Transferencia("Salão", null, "https://acme.agendeiiai.com.br", null)
            .Should().Be("Vou chamar alguém da equipe de Salão. Assim que possível, uma pessoa responde por aqui.");
    }

    [Theory]
    [InlineData("pedido_do_cliente", "o cliente pediu para falar com uma pessoa")]
    [InlineData("reclamacao", "reclamação do cliente")]
    [InlineData("fora_do_escopo", "assunto fora do que o assistente atende")]
    [InlineData("outro", "outro motivo")]
    [InlineData("qualquer coisa que o modelo escreveu", "outro motivo")]
    public void Motivo_do_email_e_sempre_um_texto_fixo(string codigo, string esperado) =>
        TextosHumanoAgente.MotivoParaEmail(codigo).Should().Be(esperado);

    // ---------------------------------------------------------------- prompt

    private static readonly DateTimeOffset AgoraLocal = new(2026, 10, 28, 9, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void Prompt_so_vira_v3_com_a_ferramenta_de_atendente_e_v1_v2_continuam_iguais()
    {
        var v1 = ConstrutorPromptAgente.Construir("Acme", AgoraLocal, "https://acme.agendeiiai.com.br");
        var v2 = ConstrutorPromptAgente.ConstruirComAgendamento("Acme", AgoraLocal, "https://acme.agendeiiai.com.br");
        v1.Should().Contain("prompt v1").And.NotContain("chamar_atendente");
        v2.Should().Contain("prompt v2").And.NotContain("chamar_atendente");
        ConstrutorPromptAgente.Construir("Acme", AgoraLocal, "https://acme.agendeiiai.com.br", false).Should().Be(v1);
        ConstrutorPromptAgente.ConstruirComAgendamento("Acme", AgoraLocal, "https://acme.agendeiiai.com.br", false).Should().Be(v2);

        var v3 = ConstrutorPromptAgente.Construir("Acme", AgoraLocal, "https://acme.agendeiiai.com.br", true);
        var v3Agendamento = ConstrutorPromptAgente.ConstruirComAgendamento("Acme", AgoraLocal, "https://acme.agendeiiai.com.br", true);
        foreach (var prompt in new[] { v3, v3Agendamento })
        {
            prompt.Should().Contain("prompt v3").And.Contain("chamar_atendente").And.Contain("pedido_do_cliente");
            prompt.Should().NotContain("prompt v1").And.NotContain("prompt v2");
        }
    }

    // ---------------------------------------------------------------- opções

    private static readonly ValidadorOpcoesIa Validador = new();

    [Theory]
    [InlineData(0, 6, 20, true)]
    [InlineData(24, 6, 20, true)]
    [InlineData(168, 72, 500, true)]
    [InlineData(-1, 6, 20, false)]
    [InlineData(169, 6, 20, false)]
    [InlineData(24, 0, 20, false)]
    [InlineData(24, 73, 20, false)]
    [InlineData(24, 6, 0, false)]
    [InlineData(24, 6, 501, false)]
    public void Opcoes_de_humano_validam_as_faixas(int devolverApos, int intervalo, int limite, bool valido)
    {
        var opcoes = new OpcoesIa
        {
            Humano = { DevolverAposHoras = devolverApos, IntervaloAvisoPorConversaHoras = intervalo, LimiteAvisosPorNegocioPorDia = limite },
        };

        Validador.Validate(null, opcoes).Succeeded.Should().Be(valido);
    }

    [Fact]
    public void Opcoes_de_humano_tem_os_padroes_do_plano()
    {
        var humano = new OpcoesIa().Humano;

        humano.FerramentaAtiva.Should().BeFalse();
        humano.DevolverAposHoras.Should().Be(24);
        humano.PrazoDevolucao.Should().Be(TimeSpan.FromHours(24));
        humano.IntervaloAvisoPorConversaHoras.Should().Be(6);
        humano.LimiteAvisosPorNegocioPorDia.Should().Be(20);
        new OpcoesHumanoAgente { DevolverAposHoras = 0 }.PrazoDevolucao.Should().BeNull();
    }

    // ---------------------------------------------------------------- ferramenta e catálogo

    [Fact]
    public void Esquema_do_chamar_atendente_so_tem_motivo_fechado_e_proibe_campos_extras()
    {
        var ferramenta = new FerramentaChamarAtendente(null!, null!, null!, null!, NullLogger<FerramentaChamarAtendente>.Instance);

        ferramenta.Nome.Should().Be("chamar_atendente");
        ferramenta.Should().BeAssignableTo<IFerramentaDeEscritaAgente>("conta no limite de uma ferramenta de escrita por rodada");
        var esquema = ferramenta.Esquema;
        esquema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        esquema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().Equal("motivo");
        esquema.GetProperty("properties").GetProperty("motivo").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo("pedido_do_cliente", "reclamacao", "fora_do_escopo", "outro");
        esquema.GetRawText().Should().NotContainAny("telefone", "negocio", "conversa", "cliente_id");
    }

    private sealed class Atendente : IFerramentaDeAtendimentoHumano
    {
        public string Nome => "chamar_atendente";

        public string Descricao => "x";

        public JsonElement Esquema { get; } = JsonDocument.Parse("{}").RootElement.Clone();

        public Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default) =>
            Task.FromResult(JsonDocument.Parse("{}").RootElement.Clone());

        public Task<ResultadoFerramentaAgente> ExecutarComSinalAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResultadoFerramentaAgente(
                JsonDocument.Parse("{}").RootElement.Clone(), Acao: new AcaoServidor(TipoAcaoServidor.Transferencia, contexto.ConversaId)));
    }

    private sealed class Contexto(Guid id) : IContextoNegocio
    {
        public Guid? NegocioId { get; private set; } = id;

        public bool TemNegocio => NegocioId is not null;

        public void Definir(Guid negocioId) => NegocioId = negocioId;
    }

    private static ExecutorFerramentasAgente Executor(Guid negocio, bool flag, ProvedorIa provedor) =>
        new([new Atendente()], new Contexto(negocio), NullLogger<ExecutorFerramentasAgente>.Instance,
            Microsoft.Extensions.Options.Options.Create(new OpcoesIa { Provider = provedor, Humano = { FerramentaAtiva = flag } }));

    [Theory]
    [InlineData(false, ProvedorIa.Gemini, false)]
    [InlineData(true, ProvedorIa.Eco, false)]
    [InlineData(true, ProvedorIa.Desligado, false)]
    [InlineData(true, ProvedorIa.Gemini, true)]
    public async Task Chamar_atendente_so_e_oferecida_com_a_flag_e_Gemini_sem_depender_do_agendamento(bool flag, ProvedorIa provedor, bool oferecida)
    {
        var negocio = Guid.NewGuid();
        var executor = Executor(negocio, flag, provedor);

        executor.FerramentasPara(negocio).Any().Should().Be(oferecida);
        var resultado = await executor.ExecutarComSinalAsync(
            new ContextoConversaAgente(negocio, Guid.NewGuid(), Guid.NewGuid()), "chamar_atendente", JsonDocument.Parse("{}").RootElement);
        if (oferecida)
            resultado.Acao.Should().NotBeNull();
        else
            resultado.Conteudo.GetProperty("erro").GetString().Should().Be("ferramenta_desconhecida");
    }
}
