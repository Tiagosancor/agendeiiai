using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plataforma.Api.Controllers.Publico;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Dominio.Verificacao;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Notificacoes;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Notificacoes;

/// <summary>
/// Provedor EvolutionApi e aviso ao profissional por WhatsApp (item 6 da seção 14 — seções 4,
/// 8.1, 9 e 10). Testes obrigatórios: instância fora do ar não impede o e-mail nem dá erro ao
/// cliente; webhook idempotente e só com o segredo; reenvio limitado sem revelar o motivo; job de
/// expiração libera a reserva quando nenhum canal chegou; aviso ao profissional por WhatsApp só
/// com a opção ligada, e-mail sempre.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class WhatsAppEvolutionTestes : IAsyncLifetime
{
    private const string DominioBase = DominioDeTeste.Valor;
    private const string SegredoWebhook = "segredo-do-webhook-de-teste-0123456789";

    /// <summary>Provedor EvolutionApi ligado (com a flag), para o webhook existir. O envio continua no espião.</summary>
    private static readonly Dictionary<string, string?> ConfiguracaoEvolution = new()
    {
        ["WhatsApp:Provedor"] = "EvolutionApi",
        ["WhatsApp:PermitirNaoOficial"] = "true",
        ["WhatsApp:Evolution:UrlBase"] = "http://evolution.invalid",
        ["WhatsApp:Evolution:ChaveApi"] = "chave-de-teste",
        ["WhatsApp:Evolution:NomeInstancia"] = "teste",
        ["WhatsApp:Evolution:SegredoWebhook"] = SegredoWebhook,
    };

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public WhatsAppEvolutionTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, ConfiguracaoEvolution);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private EspiaEmail Email => _fabrica.Services.GetRequiredService<EspiaEmail>();

    private EspiaWhatsApp WhatsApp => _fabrica.Services.GetRequiredService<EspiaWhatsApp>();

    // ---------------------------------------------------------------- (a) envio em paralelo

    [Fact]
    public async Task Evolution_fora_do_ar_nao_impede_o_email_nem_da_erro_ao_cliente()
    {
        // Provedor REAL, com a instância fora do ar (toda chamada HTTP falha) — prova o retry
        // único e que a falha fica contida no canal.
        var instancia = new HandlerForaDoAr();
        await using var fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, ConfiguracaoEvolution, servicos =>
            servicos.AddScoped<IMensageriaWhatsApp>(_ => new MensageriaWhatsAppEvolution(
                new HttpClient(instancia), Options.Create(new OpcoesWhatsApp { Evolution = OpcoesDeTeste() }),
                NullLogger<MensageriaWhatsAppEvolution>.Instance)));

        var (slug, _) = await SemearNegocioAsync(fabrica);
        using var cliente = ClientePara(fabrica, slug);
        var telefone = TelefoneAleatorio();
        var email = $"cliente-{Guid.NewGuid():N}@teste.com";

        var resposta = await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, email));

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        instancia.Chamadas.Should().Be(2, "uma tentativa e um único retry automático");

        var corpoEmail = fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Single(e => e.Destinatario == email).CorpoHtml;
        var codigo = ExtrairCodigo(corpoEmail);

        var registro = await CodigoMaisRecenteAsync(fabrica, telefone);
        registro.CanalWhatsAppStatus.Should().Be(StatusCanal.Falhou);
        registro.CanalEmailStatus.Should().Be(StatusCanal.Enviado);

        // E o cliente confirma pelo código do e-mail.
        var validar = await cliente.PostAsJsonAsync("/publico/codigos/validar", new CodigosPublicoController.ValidarCodigoRequisicao(telefone, codigo));
        validar.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Email_fora_do_ar_nao_impede_o_whatsapp()
    {
        var (slug, _) = await SemearNegocioAsync(_fabrica);
        using var cliente = ClientePara(_fabrica, slug);
        var telefone = TelefoneAleatorio();

        Email.Falhar = true;
        try
        {
            var resposta = await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, "a@teste.com"));
            resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }
        finally
        {
            Email.Falhar = false;
        }

        WhatsApp.Enviados.Should().Contain(e => e.Telefone.Valor == telefone);
        var registro = await CodigoMaisRecenteAsync(_fabrica, telefone);
        registro.CanalWhatsAppStatus.Should().Be(StatusCanal.Enviado);
        registro.CanalEmailStatus.Should().Be(StatusCanal.Falhou);
    }

    // ---------------------------------------------------------------- (b) webhook de status

    [Fact]
    public async Task Webhook_sem_o_segredo_ou_com_segredo_errado_e_recusado()
    {
        var (slug, _) = await SemearNegocioAsync(_fabrica);
        var telefone = TelefoneAleatorio();
        var idMensagem = await SolicitarCodigoComIdAsync(slug, telefone);

        using var anonimo = _fabrica.CreateClient();
        (await anonimo.PostAsync("/webhooks/evolution/status", CorpoStatus(idMensagem, "DELIVERY_ACK"))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.PostAsync("/webhooks/evolution/status?token=errado", CorpoStatus(idMensagem, "DELIVERY_ACK"))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        (await CodigoMaisRecenteAsync(_fabrica, telefone)).CanalWhatsAppStatus.Should().Be(StatusCanal.Pendente);
        (await ContarEventosAsync(idMensagem)).Should().Be(0);
    }

    [Fact]
    public async Task Webhook_atualiza_o_status_e_evento_repetido_nao_muda_nada()
    {
        var (slug, _) = await SemearNegocioAsync(_fabrica);
        var telefone = TelefoneAleatorio();
        var idMensagem = await SolicitarCodigoComIdAsync(slug, telefone);
        (await CodigoMaisRecenteAsync(_fabrica, telefone)).CanalWhatsAppStatus.Should().Be(StatusCanal.Pendente);

        (await PostarWebhookAsync(CorpoStatus(idMensagem, "DELIVERY_ACK"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var depoisDoPrimeiro = await CodigoMaisRecenteAsync(_fabrica, telefone);
        depoisDoPrimeiro.CanalWhatsAppStatus.Should().Be(StatusCanal.Enviado);

        // A instância reenvia o mesmo evento: 200 (para ela parar), sem efeito nenhum.
        (await PostarWebhookAsync(CorpoStatus(idMensagem, "DELIVERY_ACK"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var depoisDoRepetido = await CodigoMaisRecenteAsync(_fabrica, telefone);
        depoisDoRepetido.CanalWhatsAppStatus.Should().Be(StatusCanal.Enviado);
        depoisDoRepetido.AtualizadoEm.Should().Be(depoisDoPrimeiro.AtualizadoEm, "o registro nem foi gravado de novo");
        (await ContarEventosAsync(idMensagem)).Should().Be(1);

        // Um "falhou" atrasado também não desfaz o que já foi registrado.
        (await PostarWebhookAsync(CorpoStatus(idMensagem, "ERROR"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CodigoMaisRecenteAsync(_fabrica, telefone)).CanalWhatsAppStatus.Should().Be(StatusCanal.Enviado);
    }

    [Fact]
    public async Task Webhook_que_chega_antes_de_o_envio_ser_gravado_nao_se_perde()
    {
        var (slug, _) = await SemearNegocioAsync(_fabrica);
        var telefone = TelefoneAleatorio();
        var idMensagem = $"MSG-{Guid.NewGuid():N}";

        // O status chega primeiro (a instância foi mais rápida que o SaveChanges da API).
        (await PostarWebhookAsync(CorpoStatus(idMensagem, "SERVER_ACK"))).StatusCode.Should().Be(HttpStatusCode.OK);

        await SolicitarCodigoComIdAsync(slug, telefone, idMensagem);

        (await CodigoMaisRecenteAsync(_fabrica, telefone)).CanalWhatsAppStatus.Should().Be(StatusCanal.Enviado);
    }

    [Fact]
    public async Task Webhook_nao_existe_quando_o_provedor_nao_e_o_evolution()
    {
        await using var fabricaFake = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var cliente = fabricaFake.CreateClient();

        var resposta = await cliente.PostAsync($"/webhooks/evolution/status?token={SegredoWebhook}", CorpoStatus("X", "DELIVERY_ACK"));

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- (d) reenvio

    [Fact]
    public async Task Reenvio_respeita_o_limite_e_nega_o_quarto_pedido_sem_revelar_o_motivo()
    {
        var (slug, _) = await SemearNegocioAsync(_fabrica);
        using var cliente = ClientePara(_fabrica, slug);
        var telefone = TelefoneAleatorio();
        var requisicao = new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, "reenvio@teste.com");

        // O WhatsApp "falhou" em todos os envios — mesmo assim a resposta não fala dele.
        WhatsApp.Resultado = ResultadoEnvioWhatsApp.Indisponivel;
        try
        {
            (await cliente.PostAsJsonAsync("/publico/codigos", requisicao)).StatusCode.Should().Be(HttpStatusCode.Accepted);
            var primeiroCodigo = UltimoCodigoPorEmail("reenvio@teste.com");

            for (var i = 1; i <= 3; i++)
                (await cliente.PostAsJsonAsync("/publico/codigos/reenviar", requisicao)).StatusCode.Should().Be(HttpStatusCode.Accepted, $"reenvio {i} de 3");

            var quarto = await cliente.PostAsJsonAsync("/publico/codigos/reenviar", requisicao);
            quarto.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            var mensagem = (await quarto.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString();
            mensagem.Should().Be(CodigosPublicoController.MensagemLimiteReenvios);
            mensagem.Should().NotContainEquivalentOf("whatsapp");

            (await CodigoMaisRecenteAsync(_fabrica, telefone)).TentativasReenvio.Should().Be(3);

            // Cada reenvio troca o código: o primeiro não vale mais, o último vale.
            var comPrimeiro = await cliente.PostAsJsonAsync("/publico/codigos/validar", new CodigosPublicoController.ValidarCodigoRequisicao(telefone, primeiroCodigo));
            comPrimeiro.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var comUltimo = await cliente.PostAsJsonAsync("/publico/codigos/validar",
                new CodigosPublicoController.ValidarCodigoRequisicao(telefone, UltimoCodigoPorEmail("reenvio@teste.com")));
            comUltimo.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            WhatsApp.Restaurar();
        }
    }

    // ---------------------------------------------------------------- job de expiração (8.2)

    [Fact]
    public async Task Job_de_expiracao_libera_a_reserva_quando_nenhum_canal_entregou_o_codigo()
    {
        var (slug, negocioId) = await SemearNegocioAsync(_fabrica);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        using var cliente = ClientePara(_fabrica, slug);
        var telefone = TelefoneAleatorio();
        var inicio = AsDataHora(ProximaSegundaFeira(), new TimeOnly(10, 0));

        var reserva = await cliente.PostAsJsonAsync("/publico/reservas",
            new AgendamentosPublicoController.CriarReservaRequisicao(cenario.ProfissionalId, [cenario.ServicoId], inicio));
        reserva.EnsureSuccessStatusCode();
        var agendamentoId = (await reserva.Content.ReadFromJsonAsync<Dictionary<string, Guid>>())!["agendamentoId"];

        WhatsApp.Resultado = ResultadoEnvioWhatsApp.Indisponivel;
        Email.Falhar = true;
        try
        {
            (await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, "x@teste.com")))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);
        }
        finally
        {
            WhatsApp.Restaurar();
            Email.Falhar = false;
        }

        var registro = await CodigoMaisRecenteAsync(_fabrica, telefone);
        registro.CanalWhatsAppStatus.Should().Be(StatusCanal.Falhou);
        registro.CanalEmailStatus.Should().Be(StatusCanal.Falhou);

        // O prazo da reserva passa sem confirmação; o job (seção 8.2.2) libera o horário.
        using (var escopo = _fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            var agendamento = await db.Agendamentos.IgnoreQueryFilters().FirstAsync(a => a.Id == agendamentoId);
            typeof(Agendamento).GetProperty(nameof(Agendamento.ReservadoAte))!.SetValue(agendamento, DateTimeOffset.UtcNow.AddMinutes(-1));
            await db.SaveChangesAsync();
        }

        using (var escopo = _fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<JobExpirarReservas>().ExecutarAsync();

        using (var escopo = _fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            (await db.Agendamentos.IgnoreQueryFilters().FirstAsync(a => a.Id == agendamentoId)).Status.Should().Be(StatusAgendamento.Expirado);
        }

        // E o horário volta a ser oferecido: outra reserva no mesmo início passa.
        var novaReserva = await cliente.PostAsJsonAsync("/publico/reservas",
            new AgendamentosPublicoController.CriarReservaRequisicao(cenario.ProfissionalId, [cenario.ServicoId], inicio));
        novaReserva.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---------------------------------------------------------------- (e) aviso ao profissional

    [Fact]
    public async Task Aviso_ao_profissional_por_whatsapp_nao_sai_com_a_opcao_desligada_mas_o_email_sai()
    {
        var (painel, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var telefoneProfissional = TelefoneAleatorio();
        await PrepararProfissionalAsync(cenario.ProfissionalId, "prof-desligado@teste.com", telefoneProfissional, negocioId, avisoPorWhatsApp: false);

        var resposta = await painel.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(ProximaSegundaFeira(), new TimeOnly(10, 0))));
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        Email.Enviados.Should().ContainSingle(e => e.Destinatario == "prof-desligado@teste.com");
        WhatsApp.Enviados.Should().NotContain(e => e.Telefone.Valor == telefoneProfissional);

        var aviso = await AvisoDoAgendamentoAsync(await resposta.Content.ReadFromJsonAsync<Guid>());
        aviso.CanalEmailStatus.Should().Be(StatusCanal.Enviado);
        aviso.CanalWhatsAppStatus.Should().BeNull();
    }

    [Fact]
    public async Task Aviso_ao_profissional_com_a_opcao_ligada_sai_pelos_dois_canais_e_o_webhook_grava_a_falha()
    {
        var (painel, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var telefoneProfissional = TelefoneAleatorio();
        await PrepararProfissionalAsync(cenario.ProfissionalId, "prof-ligado@teste.com", telefoneProfissional, negocioId, avisoPorWhatsApp: true);

        var idMensagem = $"MSG-{Guid.NewGuid():N}";
        WhatsApp.Resultado = () => ResultadoEnvioWhatsApp.AguardandoConfirmacao(idMensagem);
        HttpResponseMessage resposta;
        try
        {
            resposta = await painel.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
                cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(ProximaSegundaFeira(), new TimeOnly(11, 0))));
        }
        finally
        {
            WhatsApp.Restaurar();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        Email.Enviados.Should().ContainSingle(e => e.Destinatario == "prof-ligado@teste.com");
        WhatsApp.Enviados.Should().ContainSingle(e => e.Telefone.Valor == telefoneProfissional)
            .Which.Mensagem.Should().Contain("Novo agendamento").And.Contain("às 11:00");

        var agendamentoId = await resposta.Content.ReadFromJsonAsync<Guid>();
        (await AvisoDoAgendamentoAsync(agendamentoId)).CanalWhatsAppStatus.Should().Be(StatusCanal.Pendente);

        // A instância avisa que não conseguiu entregar (ex.: número sem WhatsApp) — segredo pela URL.
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.PostAsync($"/webhooks/evolution/status?token={SegredoWebhook}", CorpoStatus(idMensagem, "ERROR")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var aviso = await AvisoDoAgendamentoAsync(agendamentoId);
        aviso.CanalWhatsAppStatus.Should().Be(StatusCanal.Falhou);
        aviso.CanalEmailStatus.Should().Be(StatusCanal.Enviado);
    }

    // ---------------------------------------------------------------- (f) saúde da instância

    [Fact]
    public async Task Saude_do_whatsapp_so_para_o_dono_da_plataforma_e_mostra_a_instancia_fora_do_ar()
    {
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.GetAsync("/plataforma/whatsapp/saude")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // O token do PAINEL não serve aqui (esquema próprio da plataforma).
        var (painel, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        (await painel.GetAsync("/plataforma/whatsapp/saude")).StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

        using (var escopo = _fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<Plataforma.Aplicacao.Administracao.IAdministracaoPlataforma>()
                .CriarOuRedefinirAdministradorAsync("dono-whatsapp@plataforma.dev", "Dono", "SenhaForte123");

        var login = await anonimo.PostAsJsonAsync("/plataforma/auth/login", new { email = "dono-whatsapp@plataforma.dev", senha = "SenhaForte123" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        using var dono = _fabrica.CreateClient();
        dono.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // ".invalid" nunca resolve (domínio reservado): a tela mostra "indisponível", não um erro.
        var saude = await dono.GetFromJsonAsync<JsonElement>("/plataforma/whatsapp/saude");
        saude.GetProperty("provedor").GetString().Should().Be("EvolutionApi");
        saude.GetProperty("estado").GetString().Should().Be("Indisponivel");
    }

    // ---------------------------------------------------------------- apoio

    private static OpcoesEvolutionApi OpcoesDeTeste() => new()
    {
        UrlBase = "http://evolution.invalid",
        ChaveApi = "chave-de-teste",
        NomeInstancia = "teste",
        SegredoWebhook = SegredoWebhook,
        TimeoutSegundos = 2,
    };

    private async Task<string> SolicitarCodigoComIdAsync(string slug, string telefone, string? idMensagem = null)
    {
        idMensagem ??= $"MSG-{Guid.NewGuid():N}";
        WhatsApp.Resultado = () => ResultadoEnvioWhatsApp.AguardandoConfirmacao(idMensagem);
        try
        {
            using var cliente = ClientePara(_fabrica, slug);
            var resposta = await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, null));
            resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }
        finally
        {
            WhatsApp.Restaurar();
        }

        return idMensagem;
    }

    private async Task<HttpResponseMessage> PostarWebhookAsync(HttpContent corpo)
    {
        using var anonimo = _fabrica.CreateClient();
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/webhooks/evolution/status") { Content = corpo };
        requisicao.Headers.Add("X-Webhook-Token", SegredoWebhook);
        return await anonimo.SendAsync(requisicao);
    }

    /// <summary>Formato do Evolution API v2 (<c>messages.update</c>).</summary>
    private static StringContent CorpoStatus(string idMensagem, string status) => new(
        JsonSerializer.Serialize(new
        {
            @event = "messages.update",
            instance = "teste",
            data = new { keyId = idMensagem, remoteJid = "5571999999999@s.whatsapp.net", fromMe = true, status },
        }),
        Encoding.UTF8, "application/json");

    private async Task<int> ContarEventosAsync(string idMensagem)
    {
        using var escopo = _fabrica.Services.CreateScope();
        return await escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().EventosWebhookWhatsApp.CountAsync(e => e.IdMensagem == idMensagem);
    }

    private static async Task<CodigoVerificacao> CodigoMaisRecenteAsync(PlataformaWebApplicationFactory fabrica, string telefone)
    {
        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var e164 = TelefoneE164.Criar(telefone);
        return await db.CodigosVerificacao.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Telefone == e164)
            .OrderByDescending(c => c.CriadoEm)
            .FirstAsync();
    }

    private async Task<NotificacaoProfissional> AvisoDoAgendamentoAsync(Guid agendamentoId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        return await escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().NotificacoesProfissional
            .IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.AgendamentoId == agendamentoId);
    }

    private async Task PrepararProfissionalAsync(Guid profissionalId, string email, string telefone, Guid negocioId, bool avisoPorWhatsApp)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var profissional = await db.Profissionais.IgnoreQueryFilters().FirstAsync(p => p.Id == profissionalId);
        profissional.AtualizarDados(profissional.Nome, telefone, email, profissional.Endereco);
        var negocio = await db.Negocios.FirstAsync(n => n.Id == negocioId);
        negocio.DefinirAvisoProfissionalPorWhatsApp(avisoPorWhatsApp);
        await db.SaveChangesAsync();
    }

    private string UltimoCodigoPorEmail(string email) => ExtrairCodigo(Email.Enviados.Last(e => e.Destinatario == email).CorpoHtml);

    private static string ExtrairCodigo(string texto) => Regex.Match(texto, @"\b\d{6}\b").Value;

    private static async Task<(string Slug, Guid NegocioId)> SemearNegocioAsync(PlataformaWebApplicationFactory fabrica)
    {
        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var slug = "wa-" + Guid.NewGuid().ToString("N")[..12];
        var negocio = Negocio.Criar(Slug.Criar(slug), "Negócio WhatsApp", TipoNegocio.Barbearia);
        db.Negocios.Add(negocio);
        await db.SaveChangesAsync();
        return (slug, negocio.Id);
    }

    private static HttpClient ClientePara(PlataformaWebApplicationFactory fabrica, string slug) =>
        fabrica.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{slug}.{DominioBase}/") });

    private static string TelefoneAleatorio() => $"+55719{Random.Shared.Next(10000000, 99999999)}";

    private static DateOnly ProximaSegundaFeira()
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var diasAteSegunda = ((int)DayOfWeek.Monday - (int)hoje.DayOfWeek + 7) % 7;
        return hoje.AddDays(diasAteSegunda == 0 ? 7 : diasAteSegunda);
    }

    private static DateTimeOffset AsDataHora(DateOnly dia, TimeOnly hora) =>
        new(dia.ToDateTime(hora, DateTimeKind.Unspecified), TimeSpan.FromHours(-3));

    /// <summary>Instância do Evolution API fora do ar: toda chamada falha na rede.</summary>
    private sealed class HandlerForaDoAr : HttpMessageHandler
    {
        private int _chamadas;

        public int Chamadas => _chamadas;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _chamadas);
            throw new HttpRequestException("Connection refused (simulado).");
        }
    }
}
