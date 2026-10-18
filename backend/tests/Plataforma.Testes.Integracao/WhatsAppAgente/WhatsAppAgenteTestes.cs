using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Agente de WhatsApp, Fase 1: segredo do webhook, deduplicação (Redis e índice do banco), mensagens ignoradas
/// (fromMe, grupo, mídia), criação de conversa/mensagem, eco, eventos de conexão e endpoints do painel.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class WhatsAppAgenteTestes : IAsyncLifetime
{
    private const string Segredo = "segredo-do-agente-de-teste-0123456789";
    private const string Cliente = "5571988887777";

    private static readonly Dictionary<string, string?> Configuracao = new()
    {
        ["Whatsapp:WebhookSecret"] = Segredo,
        ["Whatsapp:UrlPublicaApi"] = "http://api.invalid",
    };

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;
    private EspiaClienteEvolution _evolution = null!;

    public WhatsAppAgenteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _evolution = new EspiaClienteEvolution();
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, Configuracao, servicos =>
            servicos.AddSingleton<IClienteEvolution>(_evolution));
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    // ---------------------------------------------------------------- segredo

    [Fact]
    public async Task Segredo_errado_retorna_401_e_nao_grava_nada()
    {
        var (_, nome) = await SemearInstanciaAsync(agenteAtivo: true);

        var resposta = await PostarWebhookAsync("segredo-errado-0123456789", MensagemRecebida(nome, "ID1", "oi"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ContarMensagensAsync()).Should().Be(0);
        _evolution.Envios.Should().BeEmpty();
    }

    [Fact]
    public async Task Sem_segredo_configurado_o_webhook_responde_404()
    {
        await using var semSegredo = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var cliente = semSegredo.CreateClient();

        var resposta = await cliente.PostAsync($"/webhooks/whatsapp/{Segredo}", new StringContent("{}", Encoding.UTF8, "application/json"));

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- mensagens

    [Fact]
    public async Task Mensagem_nova_cria_conversa_e_mensagem_no_negocio_da_instancia_e_responde_com_eco()
    {
        var (negocioId, nome) = await SemearInstanciaAsync(agenteAtivo: true);

        var resposta = await PostarWebhookAsync(Segredo, MensagemRecebida(nome, "ID-NOVA", "Quero cortar o cabelo"));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        var conversa = (await ConversasAsync()).Single();
        conversa.NegocioId.Should().Be(negocioId);
        conversa.TelefoneCliente.Should().Be("+" + Cliente);
        conversa.Status.Should().Be(StatusConversaWhatsApp.Agente);

        var mensagens = await MensagensAsync();
        mensagens.Should().ContainSingle(m => m.Papel == PapelMensagemWhatsApp.Usuario && m.IdExterno == "ID-NOVA" && m.Conteudo == "Quero cortar o cabelo");
        mensagens.Should().ContainSingle(m => m.Papel == PapelMensagemWhatsApp.Assistente && m.Conteudo == "Recebi: Quero cortar o cabelo");

        _evolution.Envios.Should().ContainSingle().Which.Should().Be((nome, "+" + Cliente, "Recebi: Quero cortar o cabelo"));
    }

    [Fact]
    public async Task Agente_desligado_salva_a_mensagem_mas_nao_responde()
    {
        var (_, nome) = await SemearInstanciaAsync(agenteAtivo: false);

        await PostarWebhookAsync(Segredo, MensagemRecebida(nome, "ID-1", "oi"));

        (await ContarMensagensAsync()).Should().Be(1);
        _evolution.Envios.Should().BeEmpty();
    }

    [Fact]
    public async Task Segunda_mensagem_do_mesmo_cliente_reaproveita_a_conversa()
    {
        var (_, nome) = await SemearInstanciaAsync(agenteAtivo: false);

        await PostarWebhookAsync(Segredo, MensagemRecebida(nome, "ID-A", "oi"));
        await PostarWebhookAsync(Segredo, MensagemRecebida(nome, "ID-B", "tudo bem?"));

        (await ConversasAsync()).Should().HaveCount(1);
        (await ContarMensagensAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Webhook_reenviado_grava_uma_unica_vez_e_responde_uma_unica_vez_so_com_o_indice_do_banco()
    {
        // Sem Redis configurado nos testes: quem barra a duplicata é o índice único (instância, id externo).
        var (_, nome) = await SemearInstanciaAsync(agenteAtivo: true);
        var corpo = MensagemRecebida(nome, "ID-REPETIDO", "oi");

        (await PostarWebhookAsync(Segredo, corpo)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostarWebhookAsync(Segredo, corpo)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await MensagensAsync()).Count(m => m.Papel == PapelMensagemWhatsApp.Usuario).Should().Be(1);
        _evolution.Envios.Should().HaveCount(1);
    }

    [Fact]
    public async Task Mesmo_id_em_instancias_diferentes_nao_conflita()
    {
        var (_, nomeA) = await SemearInstanciaAsync(agenteAtivo: false);
        var (_, nomeB) = await SemearInstanciaAsync(agenteAtivo: false);

        await PostarWebhookAsync(Segredo, MensagemRecebida(nomeA, "ID-IGUAL", "oi"));
        await PostarWebhookAsync(Segredo, MensagemRecebida(nomeB, "ID-IGUAL", "oi"));

        (await ContarMensagensAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Redis_que_acusa_duplicata_barra_antes_do_banco()
    {
        var deduplicador = new DeduplicadorEmMemoria();
        await using var fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, Configuracao, servicos =>
        {
            servicos.AddSingleton<IClienteEvolution>(_evolution);
            servicos.AddSingleton<IDeduplicadorMensagens>(deduplicador);
        });
        using var aquecimento = fabrica.CreateClient();
        var (_, nome) = await SemearInstanciaAsync(agenteAtivo: true);
        var corpo = MensagemRecebida(nome, "ID-REDIS", "oi");

        await PostarWebhookAsync(Segredo, corpo, fabrica);
        await PostarWebhookAsync(Segredo, corpo, fabrica);

        deduplicador.Consultas.Should().Be(2, "as duas entregas passaram pelo Redis");
        (await ContarMensagensAsync()).Should().Be(2, "a mensagem do cliente e o eco, uma vez só");
        _evolution.Envios.Should().HaveCount(1);
    }

    [Fact]
    public async Task Mensagem_enviada_por_nos_grupo_midia_e_difusao_sao_ignorados()
    {
        var (_, nome) = await SemearInstanciaAsync(agenteAtivo: true);

        await PostarWebhookAsync(Segredo, MensagemRecebida(nome, "ID-1", "oi", deMim: true));
        await PostarWebhookAsync(Segredo, MensagemRecebida(nome, "ID-2", "oi", jid: "120363025246125486@g.us"));
        await PostarWebhookAsync(Segredo, MensagemRecebida(nome, "ID-3", "oi", jid: "status@broadcast"));
        await PostarWebhookAsync(Segredo, MensagemRecebida(nome, "ID-4", "oi", tipo: "imageMessage"));
        await PostarWebhookAsync(Segredo, MensagemRecebida(nome, "ID-5", "   "));

        (await ConversasAsync()).Should().BeEmpty();
        (await ContarMensagensAsync()).Should().Be(0);
        _evolution.Envios.Should().BeEmpty();
    }

    [Fact]
    public async Task Instancia_desconhecida_e_ignorada_com_200()
    {
        var resposta = await PostarWebhookAsync(Segredo, MensagemRecebida("neg-inexistente", "ID-1", "oi"));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ContarMensagensAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Falha_da_Evolution_ao_responder_nao_derruba_o_webhook_e_a_mensagem_fica_salva()
    {
        var (_, nome) = await SemearInstanciaAsync(agenteAtivo: true);
        _evolution.FalharEnvio = true;

        var resposta = await PostarWebhookAsync(Segredo, MensagemRecebida(nome, "ID-1", "oi"));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await MensagensAsync()).Should().ContainSingle(m => m.Papel == PapelMensagemWhatsApp.Usuario);
    }

    [Fact]
    public async Task Negocio_vem_da_instancia_nunca_do_corpo()
    {
        var (negocioA, nomeA) = await SemearInstanciaAsync(agenteAtivo: false);
        var (_, _) = await SemearInstanciaAsync(agenteAtivo: false);
        var corpo = MensagemRecebida(nomeA, "ID-1", "oi").Replace("\"instance\"", $"\"negocioId\":\"{Guid.NewGuid()}\",\"instance\"");

        await PostarWebhookAsync(Segredo, corpo);

        (await ConversasAsync()).Single().NegocioId.Should().Be(negocioA);
    }

    // ---------------------------------------------------------------- conexão

    [Fact]
    public async Task Eventos_de_conexao_e_qrcode_atualizam_o_status_da_instancia()
    {
        var (_, nome) = await SemearInstanciaAsync(agenteAtivo: false);

        await PostarWebhookAsync(Segredo, $$$"""{"event":"QRCODE_UPDATED","instance":"{{{nome}}}","data":{"qrcode":{"base64":"data:image/png;base64,AAAA","count":1} }}""");
        (await InstanciaAsync(nome)).Status.Should().Be(StatusInstanciaWhatsApp.AguardandoQrCode);

        await PostarWebhookAsync(Segredo, $$$"""{"event":"CONNECTION_UPDATE","instance":"{{{nome}}}","sender":"5571999990000@s.whatsapp.net","data":{"state":"open","statusReason":200}}""");
        var conectada = await InstanciaAsync(nome);
        conectada.Status.Should().Be(StatusInstanciaWhatsApp.Conectada);
        conectada.Telefone.Should().Be("5571999990000");

        await PostarWebhookAsync(Segredo, $$$"""{"event":"connection.update","instance":"{{{nome}}}","data":{"state":"close"}}""");
        (await InstanciaAsync(nome)).Status.Should().Be(StatusInstanciaWhatsApp.Desconectada);
    }

    // ---------------------------------------------------------------- painel

    [Fact]
    public async Task Painel_conectar_cria_a_instancia_com_webhook_por_instancia_e_devolve_o_qr_code()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        _evolution.QrCode = "data:image/png;base64,QRQR";

        var resposta = await cliente.PostAsync("/painel/whatsapp/conectar", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await resposta.Content.ReadFromJsonAsync<ConexaoWhatsApp>();
        corpo!.QrCode.Should().Be("data:image/png;base64,QRQR");
        corpo.Status.Should().Be("AguardandoQrCode");

        var instancia = await InstanciaDoNegocioAsync(negocioId);
        instancia.Status.Should().Be(StatusInstanciaWhatsApp.AguardandoQrCode);
        instancia.AgenteAtivo.Should().BeFalse();

        var criada = _evolution.Criadas.Should().ContainSingle().Subject;
        criada.Nome.Should().Be(instancia.NomeInstancia);
        criada.UrlWebhook.Should().Be($"http://api.invalid/webhooks/whatsapp/{Segredo}");
    }

    [Fact]
    public async Task Painel_status_e_ligar_desligar_agente_valem_so_para_o_negocio_do_token()
    {
        var (clienteA, negocioA, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (clienteB, negocioB, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        (await clienteA.GetAsync("/painel/whatsapp/status")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clienteA.PutAsJsonAsync("/painel/whatsapp/agente", new { ativo = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await clienteA.PostAsync("/painel/whatsapp/conectar", null);
        (await clienteA.PutAsJsonAsync("/painel/whatsapp/agente", new { ativo = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var status = await (await clienteA.GetAsync("/painel/whatsapp/status")).Content.ReadFromJsonAsync<StatusWhatsApp>();
        status!.AgenteAtivo.Should().BeTrue();

        (await InstanciaDoNegocioAsync(negocioA)).AgenteAtivo.Should().BeTrue();
        (await clienteB.GetAsync("/painel/whatsapp/status")).StatusCode.Should().Be(HttpStatusCode.NotFound, "o outro negócio não enxerga a instância");
        (await DbSemFiltro(db => db.InstanciasWhatsApp.AnyAsync(i => i.NegocioId == negocioB))).Should().BeFalse();
    }

    [Fact]
    public async Task Painel_exige_login_e_permissao()
    {
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.PostAsync("/painel/whatsapp/conectar", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var (semPermissao, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Profissional);
        (await semPermissao.PostAsync("/painel/whatsapp/conectar", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Painel_conectar_com_Evolution_fora_do_ar_responde_502()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        _evolution.FalharCriacao = true;

        (await cliente.PostAsync("/painel/whatsapp/conectar", null)).StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await DbSemFiltro(db => db.InstanciasWhatsApp.AnyAsync(i => i.NegocioId == negocioId))).Should().BeFalse("sem instância na Evolution não grava a linha");
    }

    // ---------------------------------------------------------------- apoio

    private static string MensagemRecebida(
        string instancia, string id, string texto, bool deMim = false, string jid = Cliente + "@s.whatsapp.net", string tipo = "conversation") =>
        $$$"""
        {"event":"MESSAGES_UPSERT","instance":"{{{instancia}}}","data":{"key":{"remoteJid":"{{{jid}}}","fromMe":{{{(deMim ? "true" : "false")}}},"id":"{{{id}}}"},"pushName":"Cliente","message":{"conversation":"{{{texto}}}"},"messageType":"{{{tipo}}}","messageTimestamp":1700000000}}
        """;

    private async Task<HttpResponseMessage> PostarWebhookAsync(string segredo, string corpo, PlataformaWebApplicationFactory? fabrica = null)
    {
        using var cliente = (fabrica ?? _fabrica).CreateClient();
        return await cliente.PostAsync($"/webhooks/whatsapp/{segredo}", new StringContent(corpo, Encoding.UTF8, "application/json"));
    }

    private async Task<(Guid NegocioId, string Nome)> SemearInstanciaAsync(bool agenteAtivo)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var negocio = Negocio.Criar(Slug.Criar("wa-" + Guid.NewGuid().ToString("N")[..12]), "Negócio WhatsApp", TipoNegocio.Barbearia);
        db.Negocios.Add(negocio);

        var instancia = new InstanciaWhatsApp(negocio.Id, $"neg-{negocio.Id:N}");
        instancia.DefinirAgente(agenteAtivo);
        instancia.AtualizarStatus(StatusInstanciaWhatsApp.Conectada);
        db.InstanciasWhatsApp.Add(instancia);

        await db.SaveChangesAsync();
        return (negocio.Id, instancia.NomeInstancia);
    }

    private async Task<T> DbSemFiltro<T>(Func<PlataformaDbContext, Task<T>> consulta)
    {
        using var escopo = _fabrica.Services.CreateScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>());
    }

    private Task<List<ConversaWhatsApp>> ConversasAsync() =>
        DbSemFiltro(db => db.ConversasWhatsApp.IgnoreQueryFilters().AsNoTracking().ToListAsync());

    private Task<List<MensagemWhatsApp>> MensagensAsync() =>
        DbSemFiltro(db => db.MensagensWhatsApp.IgnoreQueryFilters().AsNoTracking().ToListAsync());

    private async Task<int> ContarMensagensAsync() => (await MensagensAsync()).Count;

    private Task<InstanciaWhatsApp> InstanciaAsync(string nome) =>
        DbSemFiltro(db => db.InstanciasWhatsApp.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.NomeInstancia == nome));

    private Task<InstanciaWhatsApp> InstanciaDoNegocioAsync(Guid negocioId) =>
        DbSemFiltro(db => db.InstanciasWhatsApp.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.NegocioId == negocioId));
}

/// <summary>Espião da Evolution: registra o que o agente pediu, sem rede.</summary>
public sealed class EspiaClienteEvolution : IClienteEvolution
{
    public List<(string Instancia, string Telefone, string Texto)> Envios { get; } = [];

    public List<(string Nome, string? UrlWebhook)> Criadas { get; } = [];

    public string? QrCode { get; set; } = "data:image/png;base64,TESTE";

    public bool FalharEnvio { get; set; }

    public bool FalharCriacao { get; set; }

    public Task<string?> CriarInstanciaAsync(string nomeInstancia, string? urlWebhook, CancellationToken cancellationToken = default)
    {
        if (FalharCriacao)
            throw new FalhaEvolutionException("fora do ar");

        Criadas.Add((nomeInstancia, urlWebhook));
        return Task.FromResult(QrCode);
    }

    public Task<string?> ObterQrCodeAsync(string nomeInstancia, CancellationToken cancellationToken = default) => Task.FromResult(QrCode);

    public Task<EstadoInstanciaEvolution> ConsultarStatusAsync(string nomeInstancia, CancellationToken cancellationToken = default) =>
        Task.FromResult(EstadoInstanciaEvolution.Conectando);

    public Task<string?> EnviarTextoAsync(string nomeInstancia, string telefoneE164, string texto, CancellationToken cancellationToken = default)
    {
        if (FalharEnvio)
            throw new FalhaEvolutionException("fora do ar");

        Envios.Add((nomeInstancia, telefoneE164, texto));
        return Task.FromResult<string?>($"ENVIO-{Envios.Count}");
    }
}

/// <summary>Faz o papel do Redis: devolve "duplicada" a partir da segunda consulta do mesmo id.</summary>
public sealed class DeduplicadorEmMemoria : IDeduplicadorMensagens
{
    private readonly HashSet<string> _vistos = [];

    public int Consultas { get; private set; }

    public Task<bool> RegistrarSeNovaAsync(string nomeInstancia, string idMensagem, CancellationToken cancellationToken = default)
    {
        Consultas++;
        return Task.FromResult(_vistos.Add($"{nomeInstancia}:{idMensagem}"));
    }
}
