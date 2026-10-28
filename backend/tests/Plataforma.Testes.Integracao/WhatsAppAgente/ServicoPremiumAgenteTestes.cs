using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Administracao;
using Plataforma.Dominio.Administracao;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Usuarios;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.WhatsAppAgente.HelpersAgendamentoAgente;
using static Plataforma.Testes.Integracao.WhatsAppAgente.ProvedorIaRoteirizado;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Serviço premium do agente de IA (Fase 4, Fatia 1b). Sem a contratação nada da IA roda: 0 chamadas ao provedor, 0 usos_ia,
/// 0 pendências/agendamentos/clientes, e o cliente recebe uma mensagem fixa com limites. Provedor roteirizado — nunca Gemini real.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ServicoPremiumAgenteTestes : IAsyncLifetime
{
    private const string Marcador = "indisponivel_aviso";

    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public ServicoPremiumAgenteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => IniciarAsync();

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    private async Task IniciarAsync(Dictionary<string, string?>? extra = null, string provedorIa = "Gemini", bool logs = false)
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
        await _ambiente.IniciarAsync(config, capturarLogs: logs, provedorIa: provedorIa);
    }

    private Task<List<MensagemWhatsApp>> AvisosAsync() => _ambiente.MarcadoresAsync(Marcador);

    private IEnumerable<string> TextosEnviados() => _ambiente.Evolution.Envios.Select(e => e.Texto);

    private Task EnvelhecerAvisosAsync(int horas) =>
        _ambiente.NoBancoAsync(async db =>
        {
            var antes = DateTimeOffset.UtcNow.AddHours(-horas);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE mensagens_whatsapp SET criado_em = {antes} WHERE nome_ferramenta = {Marcador}");
            return 0;
        });

    private async Task NadaDaIaRodouAsync()
    {
        _ambiente.Provedor.Chamadas.Should().Be(0, "sem o serviço contratado o provedor nunca é chamado");
        (await _ambiente.UsosAsync()).Should().BeEmpty();
        (await _ambiente.PendenciasAsync()).Should().BeEmpty();
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty();
        (await _ambiente.ClientesAsync()).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- mensagem fixa

    [Fact]
    public async Task Nao_contratado_nao_chama_a_IA_nem_grava_nada_e_manda_a_mensagem_fixa_com_nome_e_link()
    {
        var c = await _ambiente.SemearAsync("Barbearia do Zé", contratado: false);

        (await _ambiente.EnviarAsync(c, "quero agendar um corte amanhã")).StatusCode.Should().Be(HttpStatusCode.OK);

        await NadaDaIaRodouAsync();
        var texto = _ambiente.Evolution.Envios.Should().ContainSingle().Subject.Texto;
        texto.Should().StartWith("Olá! O atendimento automático de Barbearia do Zé não está disponível no momento.");
        texto.Should().Contain("Para ver horários e agendar, acesse: ").And.Contain(c.Slug + ".");

        var mensagens = await _ambiente.MensagensAsync();
        mensagens.Should().ContainSingle(m => m.Papel == PapelMensagemWhatsApp.Usuario && m.Conteudo == "quero agendar um corte amanhã");
        (await AvisosAsync()).Should().ContainSingle().Which.Papel.Should().Be(PapelMensagemWhatsApp.Sistema);
        mensagens.Should().NotContain(m => m.Papel == PapelMensagemWhatsApp.Assistente);

        var conversa = (await _ambiente.ConversasAsync()).Single();
        conversa.ProcessadaAte.Should().NotBeNull("o lote é dado como tratado (nunca reenvia em laço)");
        conversa.AvisoAutomatizadoEm.Should().BeNull("a mensagem fixa não leva o aviso de atendimento automatizado");
    }

    [Fact]
    public async Task Mensagem_fixa_e_sanitizada_nome_do_negocio_com_link_e_telefone_nao_chega_ao_cliente()
    {
        var c = await _ambiente.SemearAsync("Barbearia golpe.ai/pix https://evil.com/x ligue 71 99999-8888", contratado: false);

        await _ambiente.EnviarAsync(c, "oi");

        var texto = _ambiente.Evolution.Envios.Should().ContainSingle().Subject.Texto;
        texto.Should().NotContain("evil.com").And.NotContain("golpe.ai").And.NotContain("99999");
        texto.Should().Contain(c.Slug + ".");
    }

    [Fact]
    public async Task Mensagem_fixa_sai_uma_vez_por_conversa_a_cada_24_horas_e_de_novo_depois()
    {
        var c = await _ambiente.SemearAsync(contratado: false);

        await _ambiente.EnviarAsync(c, "oi");
        for (var i = 0; i < 5; i++)
            await _ambiente.EnviarAsync(c, $"alguém aí {i}?");

        _ambiente.Evolution.Envios.Should().HaveCount(1, "5 mensagens em menos de 24 h não geram nova resposta");
        (await _ambiente.MensagensAsync()).Count(m => m.Papel == PapelMensagemWhatsApp.Usuario).Should().Be(6, "todas as mensagens do cliente são gravadas");

        await EnvelhecerAvisosAsync(25);
        await _ambiente.EnviarAsync(c, "e agora?");

        _ambiente.Evolution.Envios.Should().HaveCount(2, "passadas 24 h, mais uma");
        await NadaDaIaRodouAsync();
    }

    [Fact]
    public async Task Mensagem_fixa_respeita_o_limite_diario_por_negocio_o_aviso_extra_nao_e_enviado()
    {
        await IniciarAsync(new() { ["Ai:Indisponivel:LimitePorNegocioPorDia"] = "2" });
        var c = await _ambiente.SemearAsync(contratado: false);

        foreach (var telefone in new[] { "5571988880001", "5571988880002", "5571988880003" })
            await _ambiente.EnviarAsync(c, "oi", telefone);

        _ambiente.Evolution.Envios.Should().HaveCount(2, "o terceiro aviso do dia passa do limite do negócio");
        (await AvisosAsync()).Should().HaveCount(2);
        (await _ambiente.MensagensAsync()).Count(m => m.Papel == PapelMensagemWhatsApp.Usuario).Should().Be(3);
        await NadaDaIaRodouAsync();
    }

    [Fact]
    public async Task Limite_diario_de_um_negocio_nao_afeta_outro()
    {
        await IniciarAsync(new() { ["Ai:Indisponivel:LimitePorNegocioPorDia"] = "1" });
        var a = await _ambiente.SemearAsync("Negócio A", contratado: false);
        var b = await _ambiente.SemearAsync("Negócio B", contratado: false);

        await _ambiente.EnviarAsync(a, "oi", "5571988880001");
        await _ambiente.EnviarAsync(a, "oi", "5571988880002");
        await _ambiente.EnviarAsync(b, "oi", "5571988880003");

        TextosEnviados().Should().HaveCount(2);
        TextosEnviados().Last().Should().Contain("Negócio B");
    }

    // ---------------------------------------------------------------- silêncio

    [Fact]
    public async Task Agente_desligado_na_tela_continua_em_silencio_mesmo_sem_o_servico()
    {
        var c = await _ambiente.SemearAsync(agenteAtivo: false, contratado: false);

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.Evolution.Envios.Should().BeEmpty();
        (await AvisosAsync()).Should().BeEmpty();
        await NadaDaIaRodouAsync();
    }

    [Fact]
    public async Task Conversa_com_humano_fica_em_silencio_sem_o_servico()
    {
        var c = await _ambiente.SemearAsync(contratado: false);
        await _ambiente.EnviarAsync(c, "oi");
        _ambiente.Evolution.Envios.Should().HaveCount(1);

        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE conversas_whatsapp SET status = 'Humano'");
            return 0;
        });
        await EnvelhecerAvisosAsync(25);
        await _ambiente.EnviarAsync(c, "ainda aí?");

        _ambiente.Evolution.Envios.Should().HaveCount(1, "humano na conversa: silêncio, nem a mensagem fixa");
        await NadaDaIaRodouAsync();
    }

    [Fact]
    public async Task Assinatura_suspensa_e_silencio_sem_falar_de_pagamento()
    {
        var c = await _ambiente.SemearAsync(contratado: false);
        await _ambiente.NoBancoAsync(async db =>
        {
            var plano = await db.Planos.FirstAsync(p => p.Nome == "Casa Cheia");
            var agora = DateTimeOffset.UtcNow;
            var assinatura = ServicoAssinatura.IniciarTeste(c.NegocioId, plano, Periodicidade.Mensal, "teste", agora.AddDays(-45));
            ServicoAssinatura.AtualizarPorTempo(assinatura, agora);
            assinatura.Estado.Should().Be(EstadoAssinatura.Suspensa);
            db.Assinaturas.Add(assinatura);
            return await db.SaveChangesAsync();
        });

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.Evolution.Envios.Should().BeEmpty();
        await NadaDaIaRodouAsync();
    }

    [Fact]
    public async Task Provedor_desligado_e_silencio_para_todos_contratado_ou_nao()
    {
        await IniciarAsync(provedorIa: "Desligado");
        var sem = await _ambiente.SemearAsync("Sem", contratado: false);
        var com = await _ambiente.SemearAsync("Com", contratado: true);

        await _ambiente.EnviarAsync(sem, "oi", "5571988880001");
        await _ambiente.EnviarAsync(com, "oi", "5571988880002");

        _ambiente.Evolution.Envios.Should().BeEmpty();
        _ambiente.Provedor.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Contratado_fora_da_lista_global_fica_em_silencio_a_lista_so_restringe()
    {
        await IniciarAsync(new() { ["Ai:NegociosHabilitados"] = Guid.NewGuid().ToString() });
        var c = await _ambiente.SemearAsync(contratado: true);

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.Evolution.Envios.Should().BeEmpty();
        _ambiente.Provedor.Chamadas.Should().Be(0);
        (await _ambiente.UsosAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Eco_nao_contratado_recebe_a_mensagem_fixa_e_nunca_o_eco()
    {
        await IniciarAsync(provedorIa: "Eco");
        var sem = await _ambiente.SemearAsync("Sem", contratado: false);

        await _ambiente.EnviarAsync(sem, "mensagem-que-nao-pode-ecoar");

        var texto = _ambiente.Evolution.Envios.Should().ContainSingle().Subject.Texto;
        texto.Should().Contain("não está disponível no momento").And.NotContain("mensagem-que-nao-pode-ecoar");
    }

    [Fact]
    public async Task Eco_contratado_continua_ecoando_como_na_Fase_1()
    {
        await IniciarAsync(provedorIa: "Eco");
        var com = await _ambiente.SemearAsync("Com", contratado: true);

        await _ambiente.EnviarAsync(com, "mensagem-para-ecoar");

        _ambiente.Evolution.Envios.Should().ContainSingle().Which.Texto.Should().Contain("mensagem-para-ecoar");
    }

    [Fact]
    public async Task Contratado_e_na_lista_o_agente_responde_como_antes()
    {
        var c = await _ambiente.SemearAsync(contratado: true);
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Olá, posso ajudar!");

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.Provedor.Chamadas.Should().Be(1);
        (await _ambiente.UsosAsync()).Should().HaveCount(1);
        _ambiente.UltimaEntrega().Should().Contain("Olá, posso ajudar!");
        (await AvisosAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Religar_o_servico_faz_o_agente_voltar_a_responder_e_desligar_para_na_hora()
    {
        var c = await _ambiente.SemearAsync(contratado: true);
        _ambiente.Provedor.Roteiro = (_, _) => Texto("resposta do agente");

        await _ambiente.EnviarAsync(c, "primeira");
        _ambiente.Provedor.Chamadas.Should().Be(1);

        await _ambiente.ContratarAgenteIaAsync(c.NegocioId, false);
        await _ambiente.EnviarAsync(c, "segunda");
        _ambiente.Provedor.Chamadas.Should().Be(1, "desligado: nenhuma chamada nova");
        _ambiente.UltimaEntrega().Should().Contain("não está disponível no momento");

        await _ambiente.ContratarAgenteIaAsync(c.NegocioId, true);
        await _ambiente.EnviarAsync(c, "terceira");
        _ambiente.Provedor.Chamadas.Should().Be(2);
        _ambiente.UltimaEntrega().Should().Contain("resposta do agente");
    }

    [Fact]
    public async Task Rodada_registra_o_desfecho_indisponivel_e_o_silencio_por_nao_contratado()
    {
        var c = await _ambiente.SemearAsync(contratado: false);
        await _ambiente.EnviarAsync(c, "oi");
        await _ambiente.EnviarAsync(c, "oi de novo");

        var rodadas = await _ambiente.NoBancoAsync(db => db.RodadasAgente.IgnoreQueryFilters().AsNoTracking().OrderBy(r => r.IniciadaEm).ToListAsync());
        rodadas.Should().HaveCount(2);
        rodadas[0].Desfecho.Should().Be(DesfechoRodadaAgente.Indisponivel);
        rodadas[1].Desfecho.Should().Be(DesfechoRodadaAgente.Silencio);
        rodadas[1].MotivoSilencio.Should().Be(MotivoSilencioRodada.NaoContratado);
        rodadas.Should().OnlyContain(r => r.Chamadas == 0 && r.TokensEntrada == 0);
    }

    [Fact]
    public async Task Logs_da_rodada_indisponivel_nao_tem_telefone_nem_texto_do_cliente()
    {
        await IniciarAsync(logs: true);
        var c = await _ambiente.SemearAsync("Barbearia Discreta", contratado: false);

        await _ambiente.EnviarAsync(c, "texto-secreto-do-cliente-123");

        var logs = string.Join('\n', _ambiente.Logs!.Linhas);
        logs.Should().NotContain(AmbienteAgente.TelefoneCliente).And.NotContain("texto-secreto-do-cliente-123");
    }

    // ---------------------------------------------------------------- desligar no meio

    [Fact]
    public async Task Desligar_entre_o_resumo_e_o_sim_o_sim_nao_confirma_recebe_a_mensagem_fixa_e_o_job_libera_o_horario()
    {
        var c = await _ambiente.SemearAsync();
        (await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"))).Should().BeNull();
        var pendencia = (await _ambiente.PendenciasAsync()).Single();
        pendencia.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
        var chamadasAntes = _ambiente.Provedor.Chamadas;

        await _ambiente.ContratarAgenteIaAsync(c.NegocioId, false);
        await _ambiente.EnviarAsync(c, "Sim!");

        _ambiente.Provedor.Chamadas.Should().Be(chamadasAntes);
        var depois = (await _ambiente.PendenciasAsync()).Single();
        depois.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando, "o 'sim' não confirmou");
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Reservado);
        (await _ambiente.ClientesAsync()).Should().BeEmpty();
        _ambiente.UltimaEntrega().Should().Contain("não está disponível no momento");

        // A pendência expira pelo job (sem gate) e o horário volta a ficar livre.
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE pendencias_agendamento_whatsapp SET expira_em = now() - interval '1 minute'");
            return 0;
        });
        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<JobExpirarReservas>().ExecutarAsync();

        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Expirada);
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Expirado);
    }

    [Fact]
    public async Task Confirmar_rele_o_banco_e_recusa_com_o_servico_desligado_mesmo_que_o_gate_da_rodada_tenha_passado()
    {
        // A corrida: o gate da rodada já passou e o dono desligou antes da confirmação gravar. Chamada direta ao serviço.
        var c = await _ambiente.SemearAsync();
        (await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"))).Should().BeNull();
        var pendencia = (await _ambiente.PendenciasAsync()).Single();
        await _ambiente.ContratarAgenteIaAsync(c.NegocioId, false);

        using var escopo = _ambiente.Fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<Plataforma.Aplicacao.Abstracoes.IContextoNegocio>().Definir(c.NegocioId);
        var resultado = await escopo.ServiceProvider.GetRequiredService<Plataforma.Aplicacao.Agendamentos.IServicoAgendamentos>()
            .ConfirmarReservaAgenteAsync(new Plataforma.Aplicacao.Agendamentos.ConfirmarReservaAgente(pendencia.Id, Tel, null, null));

        resultado.Sucesso.Should().BeFalse();
        resultado.Motivo.Should().Be(Plataforma.Aplicacao.Agendamentos.MotivoRecusaConfirmacaoAgente.ServicoNaoContratado);
        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
        (await _ambiente.AgendamentosAsync()).Single().Status.Should().Be(StatusAgendamento.Reservado);
        (await _ambiente.ClientesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Desligar_durante_a_chamada_ao_provedor_o_texto_do_modelo_nao_e_enviado()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) =>
        {
            // O dono desliga o serviço enquanto o modelo "pensa".
            _ambiente.ContratarAgenteIaAsync(c.NegocioId, false).GetAwaiter().GetResult();
            return Texto("resposta que não pode chegar ao cliente");
        };

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.Provedor.Chamadas.Should().Be(1);
        _ambiente.Evolution.Envios.Should().BeEmpty("o desligamento vale na hora: nada é enviado");
        (await _ambiente.MensagensAsync()).Should().NotContain(m => m.Papel == PapelMensagemWhatsApp.Assistente);
        (await _ambiente.ConversasAsync()).Single().ProcessadaAte.Should().NotBeNull("o lote é marcado");
    }

    [Fact]
    public async Task Desligar_antes_de_preparar_o_agendamento_dentro_da_mesma_rodada_nada_e_gravado()
    {
        var c = await _ambiente.SemearAsync();
        var argumentos = Prepara(c, c.Ana, "10:00", "Maria Souza");
        _ambiente.Provedor.Roteiro = (_, n) =>
        {
            if (n != 1)
                return Texto("ok");

            _ambiente.ContratarAgenteIaAsync(c.NegocioId, false).GetAwaiter().GetResult();
            return Chama(("preparar_agendamento", argumentos));
        };

        await _ambiente.EnviarAsync(c, "quero agendar");

        (await _ambiente.PendenciasAsync()).Should().BeEmpty();
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty();
        (await _ambiente.ClientesAsync()).Should().BeEmpty();
        _ambiente.Evolution.Envios.Should().BeEmpty();
    }

    [Fact]
    public async Task Semeador_de_desenvolvimento_deixa_a_acme_com_o_servico_contratado()
    {
        using var escopo = _ambiente.Fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        (await db.Negocios.AnyAsync()).Should().BeFalse("o banco do teste começa vazio");

        await Plataforma.Infraestrutura.Persistencia.SemeadorDesenvolvimento.SemearAsync(
            db, escopo.ServiceProvider.GetRequiredService<Plataforma.Aplicacao.Abstracoes.ISenhaHasher>());

        var acme = await db.Negocios.AsNoTracking().SingleAsync();
        acme.AgenteIaContratado.Should().BeTrue();
        acme.AgenteIaOrigem.Should().Be(Plataforma.Dominio.Negocios.OrigemAgenteIa.Manual);
        acme.AgenteIaAlteradoPor.Should().Be("semeador");
    }

    // ---------------------------------------------------------------- painel do negócio

    private async Task<(HttpClient Cliente, Guid NegocioId)> PainelAsync()
    {
        var (cliente, negocioId, _, _) = await _ambiente.Fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        return (cliente, negocioId);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage resposta) => await resposta.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Painel_servico_ia_responde_mesmo_sem_instancia_e_isola_os_negocios()
    {
        var (a, negocioA) = await PainelAsync();
        var (b, _) = await PainelAsync();
        await _ambiente.ContratarAgenteIaAsync(negocioA);

        var daA = await a.GetAsync("/painel/whatsapp/servico-ia");
        var daB = await b.GetAsync("/painel/whatsapp/servico-ia");

        daA.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonA = await JsonAsync(daA);
        jsonA.GetProperty("contratado").GetBoolean().Should().BeTrue();
        jsonA.GetProperty("origem").GetString().Should().Be("Manual");

        daB.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonB = await JsonAsync(daB);
        jsonB.GetProperty("contratado").GetBoolean().Should().BeFalse("o painel de A nunca vê o estado de B, nem o contrário");
        jsonB.GetProperty("origem").ValueKind.Should().Be(JsonValueKind.Null);

        (await a.GetAsync("/painel/whatsapp/status")).StatusCode.Should().Be(HttpStatusCode.NotFound, "sem instância o status segue 404");
    }

    [Fact]
    public async Task Painel_servico_ia_exige_login_e_permissao()
    {
        using var anonimo = _ambiente.Fabrica.CreateClient();
        (await anonimo.GetAsync("/painel/whatsapp/servico-ia")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var (semPermissao, _, _, _) = await _ambiente.Fabrica.CriarUsuarioELogarAsync(Perfil.Profissional);
        (await semPermissao.GetAsync("/painel/whatsapp/servico-ia")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Painel_ligar_o_agente_sem_o_servico_e_409_e_desligar_sempre_vale()
    {
        var (cliente, negocioId) = await PainelAsync();
        (await cliente.PostAsync("/painel/whatsapp/conectar", null)).EnsureSuccessStatusCode();

        var recusa = await cliente.PutAsJsonAsync("/painel/whatsapp/agente", new { ativo = true });

        recusa.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(recusa)).GetProperty("codigo").GetString().Should().Be("agente_ia_nao_contratado");
        (await cliente.PutAsJsonAsync("/painel/whatsapp/agente", new { ativo = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var status = await JsonAsync(await cliente.GetAsync("/painel/whatsapp/status"));
        status.GetProperty("agenteAtivo").GetBoolean().Should().BeFalse();
        status.GetProperty("agenteIaContratado").GetBoolean().Should().BeFalse();

        await _ambiente.ContratarAgenteIaAsync(negocioId);

        (await cliente.PutAsJsonAsync("/painel/whatsapp/agente", new { ativo = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        status = await JsonAsync(await cliente.GetAsync("/painel/whatsapp/status"));
        status.GetProperty("agenteAtivo").GetBoolean().Should().BeTrue();
        status.GetProperty("agenteIaContratado").GetBoolean().Should().BeTrue();

        // Desligar o serviço pelo admin não mexe na preferência do negócio (a tela segue "ligada" e volta a valer ao religar).
        await _ambiente.ContratarAgenteIaAsync(negocioId, false);
        status = await JsonAsync(await cliente.GetAsync("/painel/whatsapp/status"));
        status.GetProperty("agenteAtivo").GetBoolean().Should().BeTrue();
        status.GetProperty("agenteIaContratado").GetBoolean().Should().BeFalse();
        (await cliente.PutAsJsonAsync("/painel/whatsapp/agente", new { ativo = false })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---------------------------------------------------------------- /plataforma

    private const string EmailDono = "dono-premium@plataforma.test";
    private const string SenhaDono = "SenhaDoDono123";

    private async Task<HttpClient> DonoAsync()
    {
        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<IAdministracaoPlataforma>().CriarOuRedefinirAdministradorAsync(EmailDono, "Dono", SenhaDono);

        using var anonimo = _ambiente.Fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/plataforma/auth/login", new { email = EmailDono, senha = SenhaDono });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await JsonAsync(login)).GetProperty("accessToken").GetString();

        var cliente = _ambiente.Fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    private Task<List<LogAuditoriaPlataforma>> LogsDoAgenteAsync(Guid negocioId) =>
        _ambiente.NoBancoAsync(db => db.LogsAuditoriaPlataforma.AsNoTracking()
            .Where(l => l.NegocioId == negocioId && (l.Acao == "LigarAgenteIa" || l.Acao == "DesligarAgenteIa"))
            .OrderBy(l => l.CriadoEm).ToListAsync());

    [Fact]
    public async Task Plataforma_liga_e_desliga_com_auditoria_e_repetir_o_estado_nao_audita_de_novo()
    {
        var dono = await DonoAsync();
        var c = await _ambiente.SemearAsync(contratado: false);

        var ligar = await dono.PutAsJsonAsync($"/plataforma/negocios/{c.NegocioId}/agente-ia", new { contratado = true, motivo = "  cortesia do lançamento " });

        ligar.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await JsonAsync(ligar);
        json.GetProperty("contratado").GetBoolean().Should().BeTrue();
        json.GetProperty("origem").GetString().Should().Be("Manual");
        json.GetProperty("alteradoPor").GetString().Should().Be($"plataforma:{EmailDono}");
        json.GetProperty("alteradoEm").ValueKind.Should().Be(JsonValueKind.String);

        var logs = await LogsDoAgenteAsync(c.NegocioId);
        var log = logs.Should().ContainSingle().Subject;
        log.Acao.Should().Be("LigarAgenteIa");
        log.Autor.Should().Be($"plataforma:{EmailDono}");
        log.Detalhes.Should().Be("de desligado para ligado; origem Manual; motivo: cortesia do lançamento");

        (await dono.PutAsJsonAsync($"/plataforma/negocios/{c.NegocioId}/agente-ia", new { contratado = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LogsDoAgenteAsync(c.NegocioId)).Should().HaveCount(1, "mesmo estado: 200 sem nova linha de auditoria");

        var desligar = await dono.PutAsJsonAsync($"/plataforma/negocios/{c.NegocioId}/agente-ia", new { contratado = false });
        desligar.StatusCode.Should().Be(HttpStatusCode.OK);
        var desligado = await JsonAsync(desligar);
        desligado.GetProperty("contratado").GetBoolean().Should().BeFalse();
        desligado.GetProperty("origem").ValueKind.Should().Be(JsonValueKind.Null);

        var depois = await LogsDoAgenteAsync(c.NegocioId);
        depois.Should().HaveCount(2);
        depois[1].Acao.Should().Be("DesligarAgenteIa");
        depois[1].Detalhes.Should().Be("de ligado para desligado; origem anterior Manual");
    }

    [Fact]
    public async Task Plataforma_dois_PUT_simultaneos_geram_uma_unica_linha_de_auditoria()
    {
        var dono = await DonoAsync();
        var c = await _ambiente.SemearAsync(contratado: false);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            dono.PutAsJsonAsync($"/plataforma/negocios/{c.NegocioId}/agente-ia", new { contratado = true })));

        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await LogsDoAgenteAsync(c.NegocioId)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Plataforma_valida_a_requisicao_e_responde_404_para_negocio_inexistente()
    {
        var dono = await DonoAsync();
        var c = await _ambiente.SemearAsync(contratado: false);

        (await dono.PutAsJsonAsync($"/plataforma/negocios/{Guid.NewGuid()}/agente-ia", new { contratado = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await dono.GetAsync($"/plataforma/negocios/{Guid.NewGuid()}/agente-ia/historico")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await dono.PutAsJsonAsync($"/plataforma/negocios/{c.NegocioId}/agente-ia", new { motivo = "x" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await dono.PutAsJsonAsync($"/plataforma/negocios/{c.NegocioId}/agente-ia", new { contratado = true, motivo = new string('x', 501) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LogsDoAgenteAsync(c.NegocioId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Plataforma_recusa_token_do_painel_e_chamada_anonima()
    {
        var c = await _ambiente.SemearAsync(contratado: false);
        var (painel, _) = await PainelAsync();
        using var anonimo = _ambiente.Fabrica.CreateClient();

        (await painel.PutAsJsonAsync($"/plataforma/negocios/{c.NegocioId}/agente-ia", new { contratado = true })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await painel.GetAsync($"/plataforma/negocios/{c.NegocioId}/agente-ia/historico")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.PutAsJsonAsync($"/plataforma/negocios/{c.NegocioId}/agente-ia", new { contratado = true })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LogsDoAgenteAsync(c.NegocioId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Plataforma_historico_vem_do_mais_recente_para_o_mais_antigo()
    {
        var dono = await DonoAsync();
        var c = await _ambiente.SemearAsync(contratado: false);
        var rota = $"/plataforma/negocios/{c.NegocioId}/agente-ia";
        await dono.PutAsJsonAsync(rota, new { contratado = true, motivo = "primeiro" });
        await Task.Delay(20);
        await dono.PutAsJsonAsync(rota, new { contratado = false, motivo = "segundo" });
        await Task.Delay(20);
        await dono.PutAsJsonAsync(rota, new { contratado = true, motivo = "terceiro" });

        var resposta = await dono.GetAsync(rota + "/historico");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var itens = (await JsonAsync(resposta)).EnumerateArray().ToList();
        itens.Select(i => i.GetProperty("acao").GetString()).Should().Equal("LigarAgenteIa", "DesligarAgenteIa", "LigarAgenteIa");
        itens[0].GetProperty("detalhes").GetString().Should().EndWith("motivo: terceiro");
        itens[2].GetProperty("detalhes").GetString().Should().EndWith("motivo: primeiro");
        itens[0].GetProperty("autor").GetString().Should().Be($"plataforma:{EmailDono}");
        itens[0].GetProperty("em").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Plataforma_lista_e_detalhe_trazem_o_servico_a_conexao_e_o_filtro()
    {
        var dono = await DonoAsync();
        var com = await _ambiente.SemearAsync("Com serviço", contratado: true);
        var sem = await _ambiente.SemearAsync("Sem serviço", contratado: false, agenteAtivo: false);

        var lista = (await JsonAsync(await dono.GetAsync("/plataforma/negocios"))).EnumerateArray().ToList();
        var itemCom = lista.Single(n => n.GetProperty("id").GetGuid() == com.NegocioId);
        var itemSem = lista.Single(n => n.GetProperty("id").GetGuid() == sem.NegocioId);
        itemCom.GetProperty("agenteIaContratado").GetBoolean().Should().BeTrue();
        itemCom.GetProperty("agenteIaOrigem").GetString().Should().Be("Manual");
        itemCom.GetProperty("whatsAppConectado").GetBoolean().Should().BeTrue();
        itemSem.GetProperty("agenteIaContratado").GetBoolean().Should().BeFalse();
        itemSem.GetProperty("agenteIaOrigem").ValueKind.Should().Be(JsonValueKind.Null);

        var soContratados = (await JsonAsync(await dono.GetAsync("/plataforma/negocios?agenteIa=true"))).EnumerateArray().Select(n => n.GetProperty("id").GetGuid()).ToList();
        soContratados.Should().Contain(com.NegocioId).And.NotContain(sem.NegocioId);
        var soSem = (await JsonAsync(await dono.GetAsync("/plataforma/negocios?agenteIa=false"))).EnumerateArray().Select(n => n.GetProperty("id").GetGuid()).ToList();
        soSem.Should().Contain(sem.NegocioId).And.NotContain(com.NegocioId);

        var detalhe = await JsonAsync(await dono.GetAsync($"/plataforma/negocios/{com.NegocioId}"));
        var agenteIa = detalhe.GetProperty("agenteIa");
        agenteIa.GetProperty("contratado").GetBoolean().Should().BeTrue();
        agenteIa.GetProperty("origem").GetString().Should().Be("Manual");
        agenteIa.GetProperty("alteradoPor").GetString().Should().Be("teste");
    }
}
