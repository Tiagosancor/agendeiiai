using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.WhatsAppAgente.HelpersAgendamentoAgente;
using static Plataforma.Testes.Integracao.WhatsAppAgente.ProvedorIaRoteirizado;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Fase 3, ferramenta <c>preparar_agendamento</c> com o "modelo" roteirizado: validação no servidor, reserva, pendência, resumo do
/// servidor no lugar do texto do modelo, rodada encerrada sem nova volta, limites e o modo (desligado/piloto/todos).
/// Nunca fala com o Gemini de verdade.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class PrepararAgendamentoAgenteTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public PrepararAgendamentoAgenteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => IniciarAsync();

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    private async Task IniciarAsync(Dictionary<string, string?>? extra = null, bool logs = false)
    {
        if (_ambiente is not null)
            await _ambiente.DisposeAsync();

        _ambiente = new AmbienteAgente(_postgres);
        var config = new Dictionary<string, string?>
        {
            ["Ai:Agendamento:Modo"] = "Todos",
            ["Ai:LimiteMensagensPorTelefonePorHora"] = "1000",
            ["Ai:LimiteMensagensPorTelefonePorDia"] = "5000",
            ["Ai:MaxChamadasFerramentaPorIteracao"] = "10",
        };
        foreach (var (chave, valor) in extra ?? new Dictionary<string, string?>())
            config[chave] = valor;
        await _ambiente.IniciarAsync(config, capturarLogs: logs);
    }

    private Task<string?> PrepararAsync(CenarioAgente c, string argumentos, string telefone = Tel, string mensagem = "quero agendar") =>
        _ambiente.PrepararAsync(c, argumentos, telefone, mensagem);

    // ---------------------------------------------------------------- caminho feliz do preparo

    [Fact]
    public async Task Cliente_novo_recebe_nome_necessario_e_depois_o_resumo_do_servidor_no_lugar_do_texto_do_modelo()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, n) => n switch
        {
            1 => Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00"))),
            2 => Texto("Qual é o seu nome completo?"),
            3 => Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Maria Souza"))),
            _ => Texto("Agendado! Está tudo confirmado."),
        };

        await _ambiente.EnviarAsync(c, "quero corte segunda às 10h com a Ana");

        Erro(_ambiente.Provedor.Requisicoes[1]).Should().Be("nome_necessario");
        _ambiente.UltimaEntrega().Should().EndWith("Qual é o seu nome completo?").And.Contain("atendimento automatizado");
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty("sem nome não há reserva");

        await _ambiente.EnviarAsync(c, "Maria Souza");

        // A rodada terminou na ferramenta: nenhuma 4ª chamada ao modelo, e o texto dele foi descartado.
        _ambiente.Provedor.Chamadas.Should().Be(3);
        var resumo = _ambiente.UltimaEntrega()!;
        resumo.Should().Contain("Confira o seu agendamento em Barbearia do Teste").And.Contain("Corte (R$ 70,00, 60 min)")
            .And.Contain("Profissional: Ana").And.Contain("segunda-feira").And.Contain("às 10:00").And.Contain("- Nome: Maria Souza")
            .And.Contain("responda SIM").And.Contain("/privacidade");
        resumo.Should().NotContain("Agendado!");

        var agendamento = (await _ambiente.AgendamentosAsync()).Single();
        agendamento.Status.Should().Be(StatusAgendamento.Reservado);
        agendamento.Origem.Should().Be(OrigemAgendamento.WhatsApp);
        agendamento.ClienteId.Should().BeNull();
        agendamento.Inicio.Should().Be(H(10, 0));
        agendamento.Fim.Should().Be(H(11, 0));

        var pendencia = (await _ambiente.PendenciasAsync()).Single();
        pendencia.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
        pendencia.AgendamentoId.Should().Be(agendamento.Id);
        pendencia.NomeInformado.Should().Be("Maria Souza");
        pendencia.ResumoEnviadoEm.Should().NotBeNull();
        pendencia.ExpiraEm.Should().Be(agendamento.ReservadoAte);

        var marcador = (await _ambiente.MarcadoresAsync(Plataforma.Infraestrutura.WhatsAppAgente.MarcadoresAgente.Resumo)).Single();
        marcador.Papel.Should().Be(PapelMensagemWhatsApp.Sistema);
        marcador.Id.Should().Be(pendencia.ResumoMensagemId!.Value);
        marcador.Conteudo.Should().Be(resumo);
    }

    [Fact]
    public async Task Cliente_existente_nao_e_perguntado_o_nome_e_nada_do_cadastro_chega_a_IA_nem_ao_resumo()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.NoBancoAsync(async db =>
        {
            db.Clientes.Add(Cliente.Criar(c.NegocioId, "Maria Cadastro", TelefoneE164.Criar(TelComMais), email: "maria.cadastro@exemplo.com"));
            return await db.SaveChangesAsync();
        });
        _ambiente.Provedor.Roteiro = (_, n) => n == 1 ? Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Outro Nome"))) : Texto("ok");

        await _ambiente.EnviarAsync(c, "quero corte segunda às 10h com a Ana");

        _ambiente.Provedor.Chamadas.Should().Be(1);
        var resumo = _ambiente.UltimaEntrega()!;
        resumo.Should().Contain("Confira o seu agendamento").And.NotContain("Maria Cadastro").And.NotContain("Outro Nome").And.NotContain("Nome:");
        (await _ambiente.PendenciasAsync()).Single().NomeInformado.Should().BeNull("cliente existente: o nome do modelo é ignorado");

        foreach (var requisicao in _ambiente.Provedor.Requisicoes)
        {
            var tudo = Serializar(requisicao);
            tudo.Should().NotContain("Maria Cadastro").And.NotContain("maria.cadastro").And.NotContain("5571988887777")
                .And.NotContain(c.NegocioId.ToString()).And.NotContain(c.NomeInstancia);
        }
    }

    [Fact]
    public async Task Horario_fora_da_oferta_devolve_alternativas_do_mesmo_dia_e_nao_reserva()
    {
        var c = await _ambiente.SemearAsync();

        var erro = await PrepararAsync(c, Prepara(c, c.Ana, "10:07", "Maria Souza"));

        erro.Should().Be("horario_indisponivel");
        var resultado = ProvedorIaRoteirizado.Resultados(_ambiente.Provedor.Requisicoes[1]).Single().Conteudo;
        var alternativas = resultado.GetProperty("alternativas").EnumerateArray().Select(a => a.GetString()!).ToList();
        alternativas.Should().HaveCountGreaterThan(0).And.HaveCountLessThanOrEqualTo(5);
        alternativas.Should().OnlyContain(a => System.Text.RegularExpressions.Regex.IsMatch(a, "^\\d\\d:\\d\\d$"));
        alternativas.Should().Contain("10:00");
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty();
        (await _ambiente.PendenciasAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("13:00", "Beto")] // Beto só trabalha de manhã
    public async Task Horario_que_o_profissional_nao_atende_e_indisponivel(string hora, string quem)
    {
        var c = await _ambiente.SemearAsync();

        (await PrepararAsync(c, Prepara(c, quem == "Beto" ? c.Beto : c.Ana, hora, "Maria Souza"))).Should().Be("horario_indisponivel");
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Horario_ocupado_e_horario_de_um_forcado_sao_indisponiveis()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.Fabrica.SemearAgendamentoAsync(c.NegocioId, c.Beto, H(9, 0), servicoId: c.Corte);

        (await PrepararAsync(c, Prepara(c, c.Beto, "09:00", "Maria Souza"))).Should().Be("horario_indisponivel");
        (await _ambiente.PendenciasAsync()).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- argumentos e isolamento

    [Theory]
    [InlineData("forcar")]
    [InlineData("telefone")]
    [InlineData("cliente_id")]
    [InlineData("negocio_id")]
    public async Task Argumento_extra_e_recusado_e_nada_e_gravado(string campo)
    {
        var c = await _ambiente.SemearAsync();
        var argumentos = Prepara(c, c.Ana, "10:00", "Maria Souza").TrimEnd('}') + ",\"" + campo + "\":\"x\"}";

        (await PrepararAsync(c, argumentos)).Should().Be("argumentos_invalidos");
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Ids_de_outro_negocio_sao_desconhecidos()
    {
        var a = await _ambiente.SemearAsync("Negócio Alfa", "Corte Alfa");
        var b = await _ambiente.SemearAsync("Negócio Beta", "Corte Beta");

        (await PrepararAsync(a, Prepara(a, a.Ana, "10:00", "Maria Souza", servico: b.Corte))).Should().Be("servico_desconhecido");
        (await PrepararAsync(a, Prepara(a, b.Ana, "10:00", "Maria Souza"))).Should().Be("profissional_desconhecido");
        (await PrepararAsync(a, Prepara(a, a.Beto, "10:00", "Maria Souza", servico: a.Barba), mensagem: "outra")).Should().BeNull("Beto faz barba: segue o preparo");
        (await _ambiente.AgendamentosAsync()).Should().OnlyContain(x => x.NegocioId == a.NegocioId);
    }

    [Theory]
    [InlineData("hora_invalida", "10h")]
    [InlineData("data_fora_do_periodo", "ontem")]
    public async Task Hora_e_data_invalidas_viram_erro_do_servidor(string esperado, string tipo)
    {
        var c = await _ambiente.SemearAsync();
        var argumentos = tipo == "10h"
            ? Prepara(c, c.Ana, "10h", "Maria Souza")
            : Prepara(c, c.Ana, "10:00", "Maria Souza", dia: AmbienteAgente.HojeNoNegocio().AddDays(-1));

        (await PrepararAsync(c, argumentos)).Should().Be(esperado);
    }

    [Fact]
    public async Task Nome_invalido_de_cliente_novo_e_recusado_sem_reservar()
    {
        var c = await _ambiente.SemearAsync();

        (await PrepararAsync(c, Prepara(c, c.Ana, "10:00", "<b>x</b> http://golpe.ai"))).Should().Be("nome_invalido");
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- uma vez por rodada, substituição, idempotência

    [Fact]
    public async Task Tres_preparos_na_mesma_volta_executam_um_e_a_rodada_termina()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, n) => n == 1
            ? Chama(
                ("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Maria Souza")),
                ("preparar_agendamento", Prepara(c, c.Ana, "11:00", "Maria Souza")),
                ("preparar_agendamento", Prepara(c, c.Beto, "09:00", "Maria Souza")))
            : Texto("ok");

        await _ambiente.EnviarAsync(c, "quero agendar");

        _ambiente.Provedor.Chamadas.Should().Be(1);
        (await _ambiente.AgendamentosAsync()).Should().ContainSingle().Which.Inicio.Should().Be(H(10, 0));
        (await _ambiente.PendenciasAsync()).Should().ContainSingle();
        (await _ambiente.MensagensAsync()).Count(m => m.NomeFerramenta == "preparar_agendamento").Should().Be(3);
    }

    [Fact]
    public async Task Preparo_que_falhou_gasta_a_unica_execucao_da_rodada()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, n) => n switch
        {
            1 => Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:07", "Maria Souza"))),
            2 => Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Maria Souza"))),
            _ => Texto("não consegui"),
        };

        await _ambiente.EnviarAsync(c, "quero agendar");

        Erro(_ambiente.Provedor.Requisicoes[2]).Should().Be("limite_chamadas");
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty("o laço entre voltas não executa o preparo de novo");
    }

    [Fact]
    public async Task Novo_preparo_substitui_o_anterior_e_libera_o_horario()
    {
        var c = await _ambiente.SemearAsync();

        (await PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"))).Should().BeNull();
        (await PrepararAsync(c, Prepara(c, c.Ana, "14:00", "Maria Souza"), mensagem: "melhor às 14h")).Should().BeNull();

        var pendencias = await _ambiente.PendenciasAsync();
        pendencias.Select(p => p.Status).Should().Equal(StatusPendenciaAgendamentoWhatsApp.Substituida, StatusPendenciaAgendamentoWhatsApp.Aguardando);
        pendencias[0].NomeInformado.Should().BeNull("o nome digitado some ao sair de Aguardando");
        var agendamentos = await _ambiente.AgendamentosAsync();
        agendamentos.Select(a => a.Status).Should().Equal(StatusAgendamento.Expirado, StatusAgendamento.Reservado);
        agendamentos[1].Inicio.Should().Be(H(14, 0));
    }

    [Fact]
    public async Task Mesmo_pedido_de_novo_nao_reserva_duas_vezes()
    {
        var c = await _ambiente.SemearAsync();
        var argumentos = Prepara(c, c.Ana, "10:00", "Maria Souza");

        (await PrepararAsync(c, argumentos)).Should().BeNull();
        (await PrepararAsync(c, argumentos, mensagem: "de novo")).Should().Be("resumo_ja_enviado");

        (await _ambiente.AgendamentosAsync()).Should().ContainSingle();
        (await _ambiente.PendenciasAsync()).Should().ContainSingle();
    }

    // ---------------------------------------------------------------- limites

    [Fact]
    public async Task Estouro_de_preparos_por_telefone_no_dia_devolve_limite_agendamentos_sem_reservar()
    {
        await IniciarAsync(new() { ["Ai:Agendamento:LimitePreparosPorTelefonePorDia"] = "2" });
        var c = await _ambiente.SemearAsync();

        (await PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"))).Should().BeNull();
        (await PrepararAsync(c, Prepara(c, c.Ana, "14:00", "Maria Souza"), mensagem: "outro")).Should().BeNull();
        (await PrepararAsync(c, Prepara(c, c.Ana, "15:00", "Maria Souza"), mensagem: "mais um")).Should().Be("limite_agendamentos");

        (await _ambiente.AgendamentosAsync()).Count(a => a.Status == StatusAgendamento.Reservado).Should().Be(1);
    }

    [Fact]
    public async Task Teto_de_reservas_ativas_do_negocio_vale_para_outros_telefones()
    {
        await IniciarAsync(new() { ["Ai:Agendamento:LimiteReservasAtivasPorNegocio"] = "2" });
        var c = await _ambiente.SemearAsync();

        (await PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"), "5571911110001")).Should().BeNull();
        (await PrepararAsync(c, Prepara(c, c.Ana, "11:00", "Maria Souza"), "5571911110002")).Should().BeNull();
        (await PrepararAsync(c, Prepara(c, c.Ana, "14:00", "Maria Souza"), "5571911110003")).Should().Be("limite_agendamentos");
        // Quem já tem a reserva pode trocá-la: a própria não conta.
        (await PrepararAsync(c, Prepara(c, c.Ana, "15:00", "Maria Souza"), "5571911110001", "troca")).Should().BeNull();
    }

    [Fact]
    public async Task Limite_de_agendamentos_futuros_por_telefone()
    {
        await IniciarAsync(new() { ["Ai:Agendamento:LimiteFuturosPorTelefone"] = "1" });
        var c = await _ambiente.SemearAsync();
        await _ambiente.NoBancoAsync(async db =>
        {
            var cliente = Cliente.Criar(c.NegocioId, "Maria", TelefoneE164.Criar(TelComMais));
            db.Clientes.Add(cliente);
            var existente = Agendamento.CriarConfirmado(
                c.NegocioId, c.Ana, cliente.Id, H(9, 0), [new ItemServicoAgendamento(c.Corte, "Corte", 50m, 30)], origem: OrigemAgendamento.WhatsApp);
            db.Agendamentos.Add(existente);
            return await db.SaveChangesAsync();
        });

        (await PrepararAsync(c, Prepara(c, c.Ana, "14:00"))).Should().Be("limite_agendamentos");
    }

    // ---------------------------------------------------------------- modo

    [Fact]
    public async Task Modo_desligado_mantem_o_comportamento_da_fase_2_prompt_v1_e_tres_ferramentas()
    {
        await IniciarAsync(new() { ["Ai:Agendamento:Modo"] = "Desligado" });
        var c = await _ambiente.SemearAsync();

        (await PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"))).Should().Be("ferramenta_desconhecida");

        var requisicao = _ambiente.Provedor.Requisicoes[0];
        requisicao.Ferramentas.Select(f => f.Nome).Should().BeEquivalentTo("listar_servicos", "listar_profissionais", "consultar_horarios");
        requisicao.PromptSistema.Should().Contain("prompt v1").And.Contain("Nesta fase você NÃO agenda").And.NotContain("preparar_agendamento");
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty();
        (await _ambiente.PendenciasAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Modo_todos_oferece_a_ferramenta_e_o_prompt_v2()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("oi");

        await _ambiente.EnviarAsync(c, "oi");

        var requisicao = _ambiente.Provedor.Requisicoes.Single();
        requisicao.Ferramentas.Select(f => f.Nome).Should().BeEquivalentTo(
            "listar_servicos", "listar_profissionais", "consultar_horarios", "preparar_agendamento");
        requisicao.PromptSistema.Should().Contain("prompt v2").And.Contain("quem confirma é o sistema");
    }

    [Fact]
    public async Task Piloto_com_lista_vazia_nao_liga_nenhum_negocio_e_com_o_negocio_na_lista_liga()
    {
        await IniciarAsync(new() { ["Ai:Agendamento:Modo"] = "Piloto" });
        var c = await _ambiente.SemearAsync();
        var outro = await _ambiente.SemearAsync("Outro Negócio");

        _ambiente.Provedor.Roteiro = (_, _) => Texto("oi");
        await _ambiente.EnviarAsync(c, "oi");
        _ambiente.Provedor.Requisicoes[0].Ferramentas.Select(f => f.Nome).Should().NotContain("preparar_agendamento");
        _ambiente.Provedor.Requisicoes[0].PromptSistema.Should().Contain("prompt v1");

        // Lista com outro negócio: este continua de fora.
        var opcoes = _ambiente.Fabrica.Services.GetRequiredService<IOptions<OpcoesIa>>().Value;
        opcoes.Agendamento.Negocios = outro.NegocioId.ToString();
        await _ambiente.EnviarAsync(c, "oi de novo");
        _ambiente.Provedor.Requisicoes[1].Ferramentas.Select(f => f.Nome).Should().NotContain("preparar_agendamento");

        opcoes.Agendamento.Negocios = $"{outro.NegocioId}, {c.NegocioId}";
        await _ambiente.EnviarAsync(c, "oi mais uma vez");
        _ambiente.Provedor.Requisicoes[2].Ferramentas.Select(f => f.Nome).Should().Contain("preparar_agendamento");
        _ambiente.Provedor.Requisicoes[2].PromptSistema.Should().Contain("prompt v2");
    }

    [Fact]
    public async Task Modo_ligado_so_vale_com_provedor_Gemini()
    {
        await _ambiente.DisposeAsync();
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(new Dictionary<string, string?> { ["Ai:Agendamento:Modo"] = "Todos" }, provedorIa: "Eco");
        var c = await _ambiente.SemearAsync();

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.Provedor.Chamadas.Should().Be(0);
        _ambiente.UltimaEntrega().Should().Be("Recebi: oi");
    }

    // ---------------------------------------------------------------- envio do resumo

    [Fact]
    public async Task Falha_definitiva_do_envio_do_resumo_libera_o_horario_e_tira_o_resumo_do_historico()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Evolution.FalharEnvio = true;

        (await PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"))).Should().BeNull();

        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Falhou);
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Expirado);
        (await _ambiente.MarcadoresAsync(Plataforma.Infraestrutura.WhatsAppAgente.MarcadoresAgente.Resumo)).Should().BeEmpty();
    }

    [Fact]
    public async Task Timeout_no_envio_do_resumo_mantem_a_pendencia_porque_o_resultado_e_ambiguo()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Evolution.AoEnviar = (_, _, _) => throw new Plataforma.Aplicacao.WhatsAppAgente.FalhaEvolutionException("sem resposta", ambigua: true);

        (await PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"))).Should().BeNull();

        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Reservado);
        (await _ambiente.MarcadoresAsync(Plataforma.Infraestrutura.WhatsAppAgente.MarcadoresAgente.Resumo)).Should().ContainSingle();
    }

    // ---------------------------------------------------------------- logs e auditoria

    [Fact]
    public async Task Nome_digitado_nao_fica_nos_argumentos_auditados_nem_nos_logs()
    {
        await IniciarAsync(logs: true);
        var c = await _ambiente.SemearAsync();

        (await PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Sigilosa"))).Should().BeNull();

        var auditoria = (await _ambiente.MensagensAsync()).Single(m => m.Papel == PapelMensagemWhatsApp.Ferramenta);
        auditoria.Conteudo.Should().NotContain("Maria Sigilosa").And.Contain("[omitido]");
        string.Join("\n", _ambiente.Logs!.Linhas).Should().NotContain("Maria Sigilosa").And.NotContain("5571988887777").And.NotContain("Confira o seu agendamento");
    }
}
