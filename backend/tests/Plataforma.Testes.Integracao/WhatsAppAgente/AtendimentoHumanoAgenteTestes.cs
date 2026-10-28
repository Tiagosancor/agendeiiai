using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.WhatsAppAgente.HelpersAgendamentoAgente;
using static Plataforma.Testes.Integracao.WhatsAppAgente.ProvedorIaRoteirizado;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Fase 4, Fatia 3: modo Humano. Assumir/devolver pelo painel (G12), devolução preguiçosa, limites e histórico desde a devolução, e a
/// ferramenta <c>chamar_atendente</c> (G13). Sempre com o modelo falso roteirizado — nunca o Gemini real.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AtendimentoHumanoAgenteTestes : IAsyncLifetime
{
    private const string SenhaAdmin = "SenhaForte!123";
    private const string Tel2 = "5571988880002";
    private const string Tel3 = "5571988880003";

    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public AtendimentoHumanoAgenteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => IniciarAsync();

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    private async Task IniciarAsync(Dictionary<string, string?>? extra = null, bool logs = false, string provedorIa = "Gemini")
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

    private EspiaEmail Email() => _ambiente.Fabrica.Services.GetRequiredService<EspiaEmail>();

    private sealed record Admin(HttpClient Cliente, string Email, Guid UsuarioId);

    /// <summary>Administrador do negócio do cenário (tem <c>AtenderConversasWhatsApp</c> por padrão), logado pela API de verdade.</summary>
    private async Task<Admin> AdminAsync(CenarioAgente c, Perfil perfil = Perfil.Administrador)
    {
        var email = $"admin-{Guid.NewGuid():N}@teste.com";
        Guid usuarioId;
        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            var usuario = Usuario.Criar(c.NegocioId, "Admin", email, perfil, escopo.ServiceProvider.GetRequiredService<ISenhaHasher>().Hash(SenhaAdmin));
            usuarioId = usuario.Id;
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
        }

        using var anonimo = _ambiente.Fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SenhaAdmin));
        login.EnsureSuccessStatusCode();
        var corpo = await login.Content.ReadFromJsonAsync<RespostaLogin>();
        var cliente = _ambiente.Fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", corpo!.AccessToken);
        return new Admin(cliente, email, usuarioId);
    }

    private async Task<ConversaWhatsApp> ConversaAsync(string telefone = Tel)
    {
        var digitos = new string(telefone.Where(char.IsAsciiDigit).ToArray());
        return (await _ambiente.ConversasAsync()).Single(x => x.TelefoneCliente.EndsWith(digitos, StringComparison.Ordinal));
    }

    private static Task<HttpResponseMessage> AssumirAsync(Admin admin, Guid conversaId) =>
        admin.Cliente.PostAsync($"/painel/whatsapp/conversas/{conversaId}/assumir", null);

    private static Task<HttpResponseMessage> DevolverAsync(Admin admin, Guid conversaId) =>
        admin.Cliente.PostAsync($"/painel/whatsapp/conversas/{conversaId}/devolver", null);

    private Task<List<RodadaAgente>> RodadasAsync() =>
        _ambiente.NoBancoAsync(db => db.RodadasAgente.IgnoreQueryFilters().AsNoTracking().OrderBy(r => r.IniciadaEm).ToListAsync());

    private Task<List<Plataforma.Dominio.Auditoria.LogAuditoriaNegocio>> AuditoriaAsync(string acao) =>
        _ambiente.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().AsNoTracking().Where(l => l.Acao == acao).ToListAsync());

    private static async Task EsperarAsync(Func<bool> condicao, int segundos = 15)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        while (!condicao() && DateTime.UtcNow < limite)
            await Task.Delay(25);
        condicao().Should().BeTrue("a condição esperada deveria acontecer em até {0}s", segundos);
    }

    private Task DefinirAsync(string sql, params object[] parametros) =>
        _ambiente.NoBancoAsync(async db => await db.Database.ExecuteSqlRawAsync(sql, parametros));

    // ---------------------------------------------------------------- assumir

    [Fact]
    public async Task Assumir_grava_as_mensagens_sem_chamar_a_IA_nem_enviar_nada_e_audita_sem_conteudo()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = await ConversaAsync();
        var chamadas = _ambiente.Provedor.Chamadas;
        var envios = _ambiente.Evolution.Envios.Count;
        var usos = (await _ambiente.UsosAsync()).Count;

        (await AssumirAsync(admin, conversa.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AssumirAsync(admin, conversa.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent, "idempotente");

        foreach (var texto in new[] { "alguém aí?", "quero falar com o dono", "sim" })
            (await _ambiente.EnviarAsync(c, texto)).StatusCode.Should().Be(HttpStatusCode.OK);

        _ambiente.Provedor.Chamadas.Should().Be(chamadas, "0 chamadas ao provedor em Humano");
        _ambiente.Evolution.Envios.Count.Should().Be(envios, "0 envios em Humano");
        (await _ambiente.UsosAsync()).Count.Should().Be(usos);
        (await _ambiente.MensagensAsync()).Count(m => m.Papel == PapelMensagemWhatsApp.Usuario).Should().Be(4, "as mensagens continuam gravadas");

        var depois = await ConversaAsync();
        depois.Status.Should().Be(StatusConversaWhatsApp.Humano);
        depois.HumanoMotivo.Should().Be(MotivoHumanoConversa.Painel);
        depois.HumanoPorUsuarioId.Should().Be(admin.UsuarioId);
        depois.HumanoDesde.Should().NotBeNull();

        var auditoria = await AuditoriaAsync("AssumirConversaWhatsApp");
        auditoria.Should().ContainSingle("a segunda chamada não mudou nada");
        auditoria[0].AutorUsuarioId.Should().Be(admin.UsuarioId);
        auditoria[0].EntidadeId.Should().Be(conversa.Id);
        auditoria[0].Detalhes.Should().BeNull("auditoria sem conteúdo de conversa");
    }

    [Fact]
    public async Task Assumir_funciona_com_o_provedor_Eco()
    {
        await IniciarAsync(provedorIa: "Eco");
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        _ambiente.Evolution.Envios.Should().HaveCount(1, "o eco respondeu antes de assumir");
        var conversa = await ConversaAsync();

        (await AssumirAsync(admin, conversa.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _ambiente.EnviarAsync(c, "e agora?");

        _ambiente.Evolution.Envios.Should().HaveCount(1, "em Humano nem o eco responde");
        (await ConversaAsync()).Status.Should().Be(StatusConversaWhatsApp.Humano);
    }

    [Fact]
    public async Task Rodada_em_andamento_no_momento_de_assumir_nao_envia_o_texto_do_modelo()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = await ConversaAsync();
        var envios = _ambiente.Evolution.Envios.Count;
        var assistente = (await _ambiente.MensagensAsync()).Count(m => m.Papel == PapelMensagemWhatsApp.Assistente);
        var chamadas = _ambiente.Provedor.Chamadas;

        _ambiente.Provedor.Atraso = TimeSpan.FromSeconds(2);
        var emAndamento = _ambiente.EnviarAsync(c, "quanto custa o corte?");
        await EsperarAsync(() => _ambiente.Provedor.Chamadas > chamadas);

        (await AssumirAsync(admin, conversa.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await emAndamento;

        _ambiente.Evolution.Envios.Count.Should().Be(envios, "o texto que o modelo produziu depois do assumir não sai");
        (await _ambiente.MensagensAsync()).Count(m => m.Papel == PapelMensagemWhatsApp.Assistente).Should().Be(assistente);
        (await RodadasAsync()).Last().Desfecho.Should().Be(DesfechoRodadaAgente.LeasePerdido);
        (await ConversaAsync()).Status.Should().Be(StatusConversaWhatsApp.Humano);
    }

    [Fact]
    public async Task Assumir_durante_a_IA_que_reserva_horario_libera_a_reserva_e_nao_envia_o_resumo()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = await ConversaAsync();
        var envios = _ambiente.Evolution.Envios.Count;
        var chamadas = _ambiente.Provedor.Chamadas;

        _ambiente.Provedor.Atraso = TimeSpan.FromSeconds(2);
        _ambiente.Provedor.Roteiro = (_, n) => n == chamadas + 1
            ? Chama(("preparar_agendamento", Prepara(c, c.Ana, "10:00", "Maria Souza")))
            : Texto("ok");
        var emAndamento = _ambiente.EnviarAsync(c, "quero agendar o corte com a Ana às 10h");
        await EsperarAsync(() => _ambiente.Provedor.Chamadas > chamadas);

        (await AssumirAsync(admin, conversa.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await emAndamento;

        _ambiente.Evolution.Envios.Count.Should().Be(envios, "o resumo não é enviado a quem foi assumido");
        var pendencias = await _ambiente.PendenciasAsync();
        pendencias.Should().NotContain(p => p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando, "a reserva não fica presa até o job expirar");
        (await _ambiente.AgendamentosAsync()).Should().NotContain(a => a.Status == StatusAgendamento.Reservado || a.Status == StatusAgendamento.Agendado);
    }

    [Fact]
    public async Task Sim_em_Humano_nao_confirma_nada_e_o_assumir_encerra_a_pendencia()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        (await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"))).Should().BeNull();
        var conversa = await ConversaAsync();
        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
        var envios = _ambiente.Evolution.Envios.Count;

        (await AssumirAsync(admin, conversa.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _ambiente.PendenciasAsync()).Single().Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Falhou);

        await _ambiente.EnviarAsync(c, "sim");

        _ambiente.Evolution.Envios.Count.Should().Be(envios);
        (await _ambiente.AgendamentosAsync()).Should().NotContain(a => a.Status == StatusAgendamento.Agendado);
        (await _ambiente.MarcadoresAsync(MarcadoresAgente.Confirmado)).Should().BeEmpty();
    }

    [Fact]
    public async Task Assumir_e_confirmacao_simultaneos_nunca_deixam_agendamento_confirmado_sem_anuncio()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        var horas = new[] { "09:00", "10:00", "13:00", "14:00" };

        for (var i = 0; i < horas.Length; i++)
        {
            var telefone = $"557198888{i:0000}";
            (await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, horas[i], "Maria Souza"), telefone)).Should().BeNull();
            var conversa = await ConversaAsync(telefone);
            var pendencia = (await _ambiente.PendenciasAsync()).Single(p => p.ConversaId == conversa.Id);

            await Task.WhenAll(AssumirAsync(admin, conversa.Id), _ambiente.EnviarAsync(c, "sim", telefone));

            var depois = (await _ambiente.PendenciasAsync()).Single(p => p.Id == pendencia.Id);
            var agendamento = (await _ambiente.AgendamentosAsync()).Single(a => a.Id == pendencia.AgendamentoId);
            var anunciada = (await _ambiente.MarcadoresAsync(MarcadoresAgente.Confirmado)).Any(m => m.ConversaId == conversa.Id);

            if (depois.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada)
            {
                agendamento.Status.Should().Be(StatusAgendamento.Agendado);
                anunciada.Should().BeTrue("confirmado e anunciado, mesmo que o assumir tenha entrado logo depois");
            }
            else
            {
                depois.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Falhou);
                agendamento.Status.Should().NotBe(StatusAgendamento.Agendado);
                anunciada.Should().BeFalse("o assumir ganhou: nada foi efetivado nem anunciado");
            }
        }
    }

    // ---------------------------------------------------------------- devolver

    [Fact]
    public async Task Devolver_nao_responde_o_acumulado_responde_a_proxima_e_o_historico_comeca_na_devolucao()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi, tudo bem?");
        var conversa = await ConversaAsync();
        await AssumirAsync(admin, conversa.Id);
        foreach (var texto in new[] { "mensagem acumulada 1", "mensagem acumulada 2", "mensagem acumulada 3", "mensagem acumulada 4", "mensagem acumulada 5" })
            await _ambiente.EnviarAsync(c, texto);
        var chamadas = _ambiente.Provedor.Chamadas;
        var envios = _ambiente.Evolution.Envios.Count;

        (await DevolverAsync(admin, conversa.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DevolverAsync(admin, conversa.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent, "idempotente");

        _ambiente.Provedor.Chamadas.Should().Be(chamadas, "devolver não processa o acumulado");
        _ambiente.Evolution.Envios.Count.Should().Be(envios);
        var devolvida = await ConversaAsync();
        devolvida.Status.Should().Be(StatusConversaWhatsApp.Agente);
        devolvida.DevolvidaEm.Should().NotBeNull();
        devolvida.HumanoDesde.Should().BeNull();
        devolvida.HumanoMotivo.Should().BeNull();
        devolvida.HumanoPorUsuarioId.Should().BeNull();
        devolvida.ProcessadaAte.Should().NotBeNull();
        (await AuditoriaAsync("DevolverConversaWhatsApp")).Should().ContainSingle().Which.Detalhes.Should().BeNull();

        await _ambiente.EnviarAsync(c, "quanto custa o corte?");

        _ambiente.Provedor.Chamadas.Should().Be(chamadas + 1, "a próxima mensagem nova é respondida");
        _ambiente.Evolution.Envios.Count.Should().Be(envios + 1);
        var pedido = _ambiente.Provedor.Requisicoes[^1];
        pedido.Mensagens.Where(m => m.Papel == Plataforma.Aplicacao.WhatsAppAgente.Ia.PapelIa.Usuario).Select(m => m.Texto)
            .Should().ContainSingle("o histórico enviado ao modelo começa em devolvida_em")
            .Which.Should().Contain("quanto custa o corte?").And.NotContain("acumulada");
    }

    [Fact]
    public async Task Limites_por_telefone_contam_so_a_partir_da_devolucao()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:LimiteMensagensPorTelefonePorHora"] = "3" });
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = await ConversaAsync();
        await AssumirAsync(admin, conversa.Id);
        for (var i = 0; i < 10; i++)
            await _ambiente.EnviarAsync(c, $"acumulada {i}");
        await DevolverAsync(admin, conversa.Id);
        var chamadas = _ambiente.Provedor.Chamadas;

        await _ambiente.EnviarAsync(c, "quanto custa o corte?");

        _ambiente.Provedor.Chamadas.Should().Be(chamadas + 1, "as 11 mensagens de antes da devolução não contam no limite de 3 por hora");
        _ambiente.UltimaEntrega().Should().NotContain("muitas mensagens");
    }

    [Fact]
    public async Task Humano_com_prazo_vencido_devolve_na_proxima_mensagem_e_processa_so_a_nova()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Humano:DevolverAposHoras"] = "1" });
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = await ConversaAsync();
        await AssumirAsync(admin, conversa.Id);
        (await ConversaAsync()).HumanoAte.Should().NotBeNull("DevolverAposHoras=1 define o prazo ao assumir");
        await _ambiente.EnviarAsync(c, "acumulada antes do prazo");
        var chamadas = _ambiente.Provedor.Chamadas;
        (await ConversaAsync()).Status.Should().Be(StatusConversaWhatsApp.Humano, "prazo vigente: segue em Humano");

        await DefinirAsync("UPDATE conversas_whatsapp SET humano_ate = now() - interval '1 minute'");
        await _ambiente.EnviarAsync(c, "voltei, quanto custa o corte?");

        var depois = await ConversaAsync();
        depois.Status.Should().Be(StatusConversaWhatsApp.Agente);
        depois.DevolvidaEm.Should().NotBeNull();
        depois.HumanoAte.Should().BeNull();
        _ambiente.Provedor.Chamadas.Should().Be(chamadas + 1, "só a mensagem nova é processada, não o acumulado");
        _ambiente.Provedor.Requisicoes[^1].Mensagens.Where(m => m.Papel == Plataforma.Aplicacao.WhatsAppAgente.Ia.PapelIa.Usuario)
            .Should().ContainSingle().Which.Texto.Should().Contain("voltei").And.NotContain("acumulada");
    }

    [Fact]
    public async Task Mensagem_do_cliente_em_Humano_adia_a_devolucao_automatica()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Humano:DevolverAposHoras"] = "24" });
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = await ConversaAsync();
        await AssumirAsync(admin, conversa.Id);
        await DefinirAsync("UPDATE conversas_whatsapp SET humano_ate = now() + interval '1 hour'");

        await _ambiente.EnviarAsync(c, "ainda preciso de ajuda");

        (await ConversaAsync()).HumanoAte.Should().BeAfter(DateTimeOffset.UtcNow.AddHours(23), "cada mensagem renova o prazo");
    }

    [Fact]
    public async Task Sem_prazo_configurado_a_conversa_nunca_volta_sozinha()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Humano:DevolverAposHoras"] = "0" });
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = await ConversaAsync();
        await AssumirAsync(admin, conversa.Id);

        (await ConversaAsync()).HumanoAte.Should().BeNull();
        var chamadas = _ambiente.Provedor.Chamadas;
        await _ambiente.EnviarAsync(c, "alguém?");
        _ambiente.Provedor.Chamadas.Should().Be(chamadas);
        (await ConversaAsync()).Status.Should().Be(StatusConversaWhatsApp.Humano);
    }

    // ---------------------------------------------------------------- rotas: negócio do token e permissão

    [Fact]
    public async Task Rotas_exigem_login_e_permissao_e_conversa_de_outro_negocio_e_404()
    {
        var a = await _ambiente.SemearAsync();
        var b = await _ambiente.SemearAsync();
        var adminA = await AdminAsync(a);
        var adminB = await AdminAsync(b);
        await _ambiente.EnviarAsync(a, "oi");
        var conversaA = await ConversaAsync();

        using var anonimo = _ambiente.Fabrica.CreateClient();
        (await anonimo.PostAsync($"/painel/whatsapp/conversas/{conversaA.Id}/assumir", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.PostAsync($"/painel/whatsapp/conversas/{conversaA.Id}/devolver", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Recepcionista não tem AtenderConversasWhatsApp por padrão.
        var recepcao = await AdminAsync(a, Perfil.Recepcionista);
        (await AssumirAsync(recepcao, conversaA.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DevolverAsync(recepcao, conversaA.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // O outro negócio não enxerga a conversa (404, igual a uma que não existe) e nada muda nela.
        (await AssumirAsync(adminB, conversaA.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DevolverAsync(adminB, conversaA.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AssumirAsync(adminA, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ConversaAsync()).Status.Should().Be(StatusConversaWhatsApp.Agente);
        (await AuditoriaAsync("AssumirConversaWhatsApp")).Should().BeEmpty();

        (await AssumirAsync(adminA, conversaA.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Assumir_conversa_anonimizada_e_404()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = await ConversaAsync();
        await DefinirAsync("UPDATE conversas_whatsapp SET telefone_cliente = 'anonimizado:abcd1234'");

        (await AssumirAsync(admin, conversa.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- chamar_atendente

    private static string Motivo(string motivo) => "{\"motivo\":\"" + motivo + "\"}";

    private void ModeloChamaAtendente(string argumentos)
    {
        var antes = _ambiente.Provedor.Chamadas;
        _ambiente.Provedor.Roteiro = (_, n) => n == antes + 1 ? Chama(("chamar_atendente", argumentos)) : Texto("ok");
    }

    [Fact]
    public async Task Chamar_atendente_leva_a_Humano_envia_texto_fixo_e_um_email_sem_conteudo_com_telefone_mascarado()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Humano:FerramentaAtiva"] = "true" }, logs: true);
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await DefinirAsync(
            "UPDATE instancias_whatsapp SET mensagem_transferencia = 'Atendemos de segunda a sexta. Veja https://golpe.com/pix e ligue 5511999990000'");
        ModeloChamaAtendente(Motivo("reclamacao"));
        var antes = _ambiente.Provedor.Chamadas;

        await _ambiente.EnviarAsync(c, "meu segredo-de-cliente: o corte ficou horrível, quero reclamar");

        _ambiente.Provedor.Chamadas.Should().Be(antes + 1, "a rodada termina na ferramenta");
        var conversa = await ConversaAsync();
        conversa.Status.Should().Be(StatusConversaWhatsApp.Humano);
        conversa.HumanoMotivo.Should().Be(MotivoHumanoConversa.PedidoDoAgente);
        conversa.HumanoPorUsuarioId.Should().BeNull();
        conversa.AtendenteAvisadoEm.Should().NotBeNull();

        var texto = _ambiente.UltimaEntrega()!;
        texto.Should().StartWith("Vou chamar alguém da equipe de Barbearia do Teste");
        texto.Should().Contain("Atendemos de segunda a sexta").And.NotContain("golpe.com").And.NotContain("5511999990000");
        (await _ambiente.MarcadoresAsync(MarcadoresAgente.Transferencia)).Should().ContainSingle();
        (await RodadasAsync()).Last().Desfecho.Should().Be(DesfechoRodadaAgente.Transferida);

        var emails = Email().Enviados.Where(e => e.Assunto.Contains("aguardando atendimento")).ToList();
        emails.Should().ContainSingle().Which.Destinatario.Should().Be(admin.Email);
        var corpo = emails[0].CorpoHtml;
        corpo.Should().NotContain("segredo-de-cliente").And.NotContain("horrível", "o e-mail não leva texto da conversa");
        corpo.Should().Contain("+5571****7777").And.NotContain("5571988887777");
        corpo.Should().Contain($"/painel/whatsapp/conversas/{conversa.Id}").And.Contain("Motivo:");
        corpo.Should().Contain("reclama", "o motivo vai como texto fixo do servidor");

        var auditoria = await AuditoriaAsync("AtenderConversaPeloAgente");
        auditoria.Should().ContainSingle().Which.Detalhes.Should().Be("motivo Reclamacao");
        auditoria[0].AutorUsuarioId.Should().BeNull();

        // Depois da transferência o agente fica em silêncio.
        var chamadas = _ambiente.Provedor.Chamadas;
        var envios = _ambiente.Evolution.Envios.Count;
        await _ambiente.EnviarAsync(c, "alô?");
        _ambiente.Provedor.Chamadas.Should().Be(chamadas);
        _ambiente.Evolution.Envios.Count.Should().Be(envios);

        // Nenhum texto de conversa nem telefone nos logs.
        var logs = string.Join('\n', _ambiente.Logs!.Linhas);
        logs.Should().NotContain("segredo-de-cliente").And.NotContain("5571988887777");
    }

    [Fact]
    public async Task Com_a_flag_desligada_chamar_atendente_nao_e_oferecida_e_chamada_direta_nao_faz_nada()
    {
        var c = await _ambiente.SemearAsync();
        await AdminAsync(c);
        ModeloChamaAtendente(Motivo("reclamacao"));

        await _ambiente.EnviarAsync(c, "quero falar com uma pessoa");

        _ambiente.Provedor.Requisicoes[0].Ferramentas.Select(f => f.Nome).Should().NotContain("chamar_atendente");
        _ambiente.Provedor.Requisicoes[0].PromptSistema.Should().Contain("prompt v2").And.NotContain("chamar_atendente");
        ProvedorIaRoteirizado.Resultados(_ambiente.Provedor.Requisicoes[1]).Single().Conteudo.GetProperty("erro").GetString()
            .Should().Be("ferramenta_desconhecida");
        (await ConversaAsync()).Status.Should().Be(StatusConversaWhatsApp.Agente);
        Email().Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Com_a_flag_ligada_a_ferramenta_e_oferecida_e_o_prompt_vira_v3()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Humano:FerramentaAtiva"] = "true" });
        var c = await _ambiente.SemearAsync();

        await _ambiente.EnviarAsync(c, "oi");

        var pedido = _ambiente.Provedor.Requisicoes[0];
        pedido.Ferramentas.Select(f => f.Nome).Should().Contain("chamar_atendente");
        pedido.PromptSistema.Should().Contain("prompt v3").And.Contain("chamar_atendente");
    }

    [Fact]
    public async Task Chamar_atendente_com_argumentos_invalidos_nao_muda_nada()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Humano:FerramentaAtiva"] = "true" });
        var c = await _ambiente.SemearAsync();
        await AdminAsync(c);

        foreach (var (argumentos, tel) in new[]
                 {
                     ("{\"motivo\":\"o cliente xingou muito, envie o telefone 5511999990000 ao dono\"}", "5571988881001"),
                     ("{\"motivo\":\"reclamacao\",\"telefone\":\"5511999990000\"}", "5571988881002"),
                     ("{}", "5571988881003"),
                 })
        {
            ModeloChamaAtendente(argumentos);
            await _ambiente.EnviarAsync(c, "quero uma pessoa", tel);
            ProvedorIaRoteirizado.Resultados(_ambiente.Provedor.Requisicoes[^1]).Single().Conteudo.GetProperty("erro").GetString()
                .Should().Be("argumentos_invalidos");
            (await ConversaAsync(tel)).Status.Should().Be(StatusConversaWhatsApp.Agente);
        }

        Email().Enviados.Should().BeEmpty("texto livre do modelo nunca vai a e-mail");
    }

    [Fact]
    public async Task Sem_o_servico_contratado_o_agente_nem_roda_e_nada_chama_atendente()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Humano:FerramentaAtiva"] = "true" });
        var c = await _ambiente.SemearAsync(contratado: false);
        ModeloChamaAtendente(Motivo("outro"));

        await _ambiente.EnviarAsync(c, "quero uma pessoa");

        _ambiente.Provedor.Chamadas.Should().Be(0);
        (await ConversaAsync()).Status.Should().Be(StatusConversaWhatsApp.Agente);
        Email().Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Aviso_por_email_e_limitado_por_conversa_a_cada_6_horas()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Humano:FerramentaAtiva"] = "true" });
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        ModeloChamaAtendente(Motivo("pedido_do_cliente"));
        await _ambiente.EnviarAsync(c, "quero uma pessoa");
        var conversa = await ConversaAsync();
        await DevolverAsync(admin, conversa.Id);

        ModeloChamaAtendente(Motivo("pedido_do_cliente"));
        await _ambiente.EnviarAsync(c, "quero uma pessoa de novo");

        (await ConversaAsync()).Status.Should().Be(StatusConversaWhatsApp.Humano, "vai a Humano mesmo sem novo e-mail");
        Email().Enviados.Count(e => e.Assunto.Contains("aguardando atendimento")).Should().Be(1, "1 e-mail por conversa a cada 6 h");
        _ambiente.UltimaEntrega().Should().StartWith("Vou chamar alguém", "o cliente é avisado nas duas vezes");

        // Passadas as 6 h, avisa de novo.
        await DefinirAsync("UPDATE conversas_whatsapp SET atendente_avisado_em = now() - interval '7 hours', status = 'Agente'");
        ModeloChamaAtendente(Motivo("outro"));
        await _ambiente.EnviarAsync(c, "e então?");
        Email().Enviados.Count(e => e.Assunto.Contains("aguardando atendimento")).Should().Be(2);
    }

    [Fact]
    public async Task Aviso_por_email_e_limitado_por_negocio_no_dia_mas_todas_as_conversas_vao_a_Humano()
    {
        await IniciarAsync(new Dictionary<string, string?>
        {
            ["Ai:Humano:FerramentaAtiva"] = "true",
            ["Ai:Humano:LimiteAvisosPorNegocioPorDia"] = "2",
        });
        var c = await _ambiente.SemearAsync();
        await AdminAsync(c);

        foreach (var tel in new[] { Tel, Tel2, Tel3 })
        {
            ModeloChamaAtendente(Motivo("reclamacao"));
            await _ambiente.EnviarAsync(c, "quero reclamar", tel);
        }

        (await _ambiente.ConversasAsync()).Should().OnlyContain(x => x.Status == StatusConversaWhatsApp.Humano);
        Email().Enviados.Count(e => e.Assunto.Contains("aguardando atendimento")).Should().Be(2, "o 3º aviso do dia não sai");
        _ambiente.Evolution.Envios.Count(e => e.Texto.StartsWith("Vou chamar alguém")).Should().Be(3, "o cliente é sempre avisado");
    }

    [Fact]
    public async Task Dono_assumiu_enquanto_a_IA_pensava_chamar_atendente_nao_envia_texto_nem_email()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Humano:FerramentaAtiva"] = "true" });
        var c = await _ambiente.SemearAsync();
        var admin = await AdminAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = await ConversaAsync();
        var envios = _ambiente.Evolution.Envios.Count;
        ModeloChamaAtendente(Motivo("pedido_do_cliente"));
        _ambiente.Provedor.Atraso = TimeSpan.FromSeconds(2);
        var chamadas = _ambiente.Provedor.Chamadas;
        var emAndamento = _ambiente.EnviarAsync(c, "quero uma pessoa");
        await EsperarAsync(() => _ambiente.Provedor.Chamadas > chamadas);

        await AssumirAsync(admin, conversa.Id);
        await emAndamento;

        _ambiente.Evolution.Envios.Count.Should().Be(envios, "quem assumiu já está atendendo: o aviso fixo não sai");
        Email().Enviados.Should().BeEmpty();
        (await ConversaAsync()).HumanoMotivo.Should().Be(MotivoHumanoConversa.Painel);
    }
}
