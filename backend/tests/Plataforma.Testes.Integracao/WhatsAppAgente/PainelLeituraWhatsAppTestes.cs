using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Usuarios;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.WhatsAppAgente.HelpersAgendamentoAgente;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Fase 4, Fatia 4: APIs de leitura do painel (conversas, uso de IA, status estendido, origem na agenda). Isolamento entre negócios,
/// permissões, ausência de texto na lista e de papel Ferramenta/notas no detalhe (G16), paginação e agregações do uso.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class PainelLeituraWhatsAppTestes : IAsyncLifetime
{
    private const string SenhaAdmin = "SenhaForte!123";
    private const string SegredoTexto = "meu-texto-secreto-do-cliente-XYZ";

    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public PainelLeituraWhatsAppTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => IniciarAsync();

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    private async Task IniciarAsync(Dictionary<string, string?>? extra = null)
    {
        if (_ambiente is not null)
            await _ambiente.DisposeAsync();

        _ambiente = new AmbienteAgente(_postgres);
        var config = new Dictionary<string, string?>
        {
            ["Ai:Agendamento:Modo"] = "Todos",
            ["Ai:LimiteMensagensPorTelefonePorHora"] = "1000",
            ["Ai:LimiteMensagensPorTelefonePorDia"] = "5000",
        };
        foreach (var (chave, valor) in extra ?? new Dictionary<string, string?>())
            config[chave] = valor;
        await _ambiente.IniciarAsync(config);
    }

    private async Task<HttpClient> LogarAsync(CenarioAgente c, Perfil perfil = Perfil.Administrador, params Permissao[] extras)
    {
        var email = $"u-{Guid.NewGuid():N}@teste.com";
        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            var usuario = Usuario.Criar(c.NegocioId, "Usuário", email, perfil, escopo.ServiceProvider.GetRequiredService<ISenhaHasher>().Hash(SenhaAdmin));
            foreach (var permissao in extras)
                usuario.ConcederPermissao(permissao);
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
        }

        using var anonimo = _ambiente.Fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SenhaAdmin));
        login.EnsureSuccessStatusCode();
        var corpo = await login.Content.ReadFromJsonAsync<RespostaLogin>();
        var cliente = _ambiente.Fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", corpo!.AccessToken);
        return cliente;
    }

    private async Task<ConversaWhatsApp> ConversaAsync(CenarioAgente c, string telefone = Tel)
    {
        var digitos = new string(telefone.Where(char.IsAsciiDigit).ToArray());
        return (await _ambiente.ConversasAsync()).Single(x => x.NegocioId == c.NegocioId && x.TelefoneCliente.EndsWith(digitos, StringComparison.Ordinal));
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage resposta)
    {
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>Insere conversas e mensagens direto no banco, com carimbos de tempo escolhidos.</summary>
    private Task<List<Guid>> InserirConversasAsync(CenarioAgente c, int quantidade, string prefixoTelefone = "+55719870")
    {
        return _ambiente.NoBancoAsync(async db =>
        {
            var instancia = await db.InstanciasWhatsApp.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.NegocioId == c.NegocioId);
            var ids = new List<Guid>();
            var baseTempo = DateTimeOffset.UtcNow.AddHours(-10);
            for (var i = 0; i < quantidade; i++)
            {
                var conversa = new ConversaWhatsApp(c.NegocioId, instancia.Id, $"{prefixoTelefone}{i:D4}", baseTempo.AddMinutes(i));
                db.ConversasWhatsApp.Add(conversa);
                ids.Add(conversa.Id);
            }

            await db.SaveChangesAsync();
            return ids;
        });
    }

    private Task InserirMensagensAsync(
        CenarioAgente c, Guid conversaId, int quantidade, PapelMensagemWhatsApp papel = PapelMensagemWhatsApp.Usuario,
        string? nomeFerramenta = null, string prefixo = "msg", DateTimeOffset? inicio = null)
    {
        return _ambiente.NoBancoAsync(async db =>
        {
            var instancia = await db.InstanciasWhatsApp.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.NegocioId == c.NegocioId);
            var baseTempo = inicio ?? DateTimeOffset.UtcNow.AddHours(-5);
            for (var i = 0; i < quantidade; i++)
            {
                var mensagem = new MensagemWhatsApp(c.NegocioId, instancia.Id, conversaId, null, papel, $"{prefixo}-{i:D3}", nomeFerramenta);
                db.Entry(mensagem).Property(m => m.CriadoEm).CurrentValue = baseTempo.AddSeconds(i);
                db.MensagensWhatsApp.Add(mensagem);
            }

            await db.SaveChangesAsync();
            return 0;
        });
    }

    // ---------------------------------------------------------------- permissões e isolamento

    [Fact]
    public async Task Rotas_exigem_login_e_permissao_e_conversa_de_outro_negocio_ou_anonimizada_e_404()
    {
        var a = await _ambiente.SemearAsync();
        var b = await _ambiente.SemearAsync();
        await _ambiente.EnviarAsync(a, SegredoTexto);
        var conversaA = await ConversaAsync(a);
        var adminA = await LogarAsync(a);
        var adminB = await LogarAsync(b);

        using var anonimo = _ambiente.Fabrica.CreateClient();
        foreach (var rota in new[]
                 {
                     "/painel/whatsapp/conversas", "/painel/whatsapp/conversas/resumo", $"/painel/whatsapp/conversas/{conversaA.Id}",
                     "/painel/whatsapp/uso",
                 })
            (await anonimo.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, rota);
        (await anonimo.PostAsync($"/painel/whatsapp/conversas/{conversaA.Id}/apagar", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.PutAsJsonAsync("/painel/whatsapp/mensagem-transferencia", new { mensagem = "x" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Recepcionista: sem AtenderConversasWhatsApp nem GerenciarConfiguracoesDoNegocio.
        var recepcao = await LogarAsync(a, Perfil.Recepcionista);
        foreach (var rota in new[]
                 {
                     "/painel/whatsapp/conversas", "/painel/whatsapp/conversas/resumo", $"/painel/whatsapp/conversas/{conversaA.Id}",
                     "/painel/whatsapp/uso",
                 })
            (await recepcao.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Forbidden, rota);
        (await recepcao.PostAsync($"/painel/whatsapp/conversas/{conversaA.Id}/apagar", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.PutAsJsonAsync("/painel/whatsapp/mensagem-transferencia", new { mensagem = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Apagar exige também GerenciarClientes: quem só tem Atender recebe 403 (e nada é apagado).
        var soAtender = await LogarAsync(a, Perfil.Profissional, Permissao.AtenderConversasWhatsApp);
        (await soAtender.GetAsync("/painel/whatsapp/conversas")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await soAtender.PostAsync($"/painel/whatsapp/conversas/{conversaA.Id}/apagar", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _ambiente.MensagensAsync()).Should().NotBeEmpty();

        // O outro negócio não vê nada do A: lista vazia, detalhe/apagar 404 (igual a um id que não existe).
        var listaB = await JsonAsync(await adminB.GetAsync("/painel/whatsapp/conversas"));
        listaB.GetProperty("total").GetInt32().Should().Be(0);
        (await adminB.GetAsync($"/painel/whatsapp/conversas/{conversaA.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await adminB.PostAsync($"/painel/whatsapp/conversas/{conversaA.Id}/apagar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await adminA.GetAsync($"/painel/whatsapp/conversas/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _ambiente.MensagensAsync()).Should().NotBeEmpty("a tentativa do outro negócio não apagou nada");

        // Anonimizada = 404 no detalhe e fora da lista.
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE conversas_whatsapp SET telefone_cliente = 'anonimizado:abcd1234'");
            return 0;
        });
        (await adminA.GetAsync($"/painel/whatsapp/conversas/{conversaA.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonAsync(await adminA.GetAsync("/painel/whatsapp/conversas"))).GetProperty("total").GetInt32().Should().Be(0);
        (await adminA.PostAsync($"/painel/whatsapp/conversas/{conversaA.Id}/apagar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- lista

    [Fact]
    public async Task Lista_nao_traz_texto_mostra_telefone_completo_e_o_nome_do_cadastro_pelas_duas_formas_do_nono_digito()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.EnviarAsync(c, SegredoTexto);
        await _ambiente.EnviarAsync(c, SegredoTexto + "-2", "5571988880002");
        await _ambiente.EnviarAsync(c, SegredoTexto + "-3", "5571988880003");
        var admin = await LogarAsync(c);

        // 1: cadastro SEM o 9 (a conversa chega com o 9); 2: cadastro com o telefone igual; 3: sem cadastro.
        await _ambiente.NoBancoAsync(async db =>
        {
            db.Clientes.Add(Cliente.Criar(c.NegocioId, "Maria Sem Nove", TelefoneE164.Criar("+557188887777")));
            db.Clientes.Add(Cliente.Criar(c.NegocioId, "João Igual", TelefoneE164.Criar("+5571988880002")));
            await db.SaveChangesAsync();
            return 0;
        });

        var resposta = await admin.GetAsync("/painel/whatsapp/conversas");
        var bruto = await resposta.Content.ReadAsStringAsync();
        bruto.Should().NotContain(SegredoTexto, "a lista não traz texto de mensagem");

        var lista = JsonDocument.Parse(bruto).RootElement;
        lista.GetProperty("total").GetInt32().Should().Be(3);
        lista.GetProperty("pagina").GetInt32().Should().Be(1);
        lista.GetProperty("tamanhoPagina").GetInt32().Should().Be(20);
        var itens = lista.GetProperty("itens").EnumerateArray().ToDictionary(i => i.GetProperty("telefone").GetString()!, i => i);
        itens.Keys.Should().BeEquivalentTo("+5571988887777", "+5571988880002", "+5571988880003");
        itens["+5571988887777"].GetProperty("clienteNome").GetString().Should().Be("Maria Sem Nove");
        itens["+5571988880002"].GetProperty("clienteNome").GetString().Should().Be("João Igual");
        itens["+5571988880003"].GetProperty("clienteNome").ValueKind.Should().Be(JsonValueKind.Null);
        itens["+5571988887777"].GetProperty("status").GetString().Should().Be("Agente");
        itens["+5571988887777"].GetProperty("mensagensDesdeHumano").GetInt32().Should().Be(0);
        itens["+5571988887777"].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "id", "telefone", "clienteNome", "status", "humanoDesde", "humanoMotivo", "humanoAte", "ultimaMensagemEm", "mensagensDesdeHumano",
            "aguardandoAtendimento");
    }

    [Fact]
    public async Task Lista_pagina_ordena_pela_mais_recente_e_filtra_por_status()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);
        var ids = await InserirConversasAsync(c, 25);

        // Duas em Humano: a 3ª aguardando (ninguém assumiu), a 5ª já assumida por um usuário.
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE conversas_whatsapp SET status = 'Humano', humano_desde = now(), humano_motivo = 'PedidoDoAgente' WHERE id = {ids[3]}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE conversas_whatsapp SET status = 'Humano', humano_desde = now(), humano_motivo = 'Painel', humano_por_usuario_id = {Guid.NewGuid()} WHERE id = {ids[5]}");
            return 0;
        });

        var pagina1 = await JsonAsync(await admin.GetAsync("/painel/whatsapp/conversas"));
        pagina1.GetProperty("total").GetInt32().Should().Be(25);
        pagina1.GetProperty("itens").GetArrayLength().Should().Be(20);
        pagina1.GetProperty("itens")[0].GetProperty("id").GetGuid().Should().Be(ids[24], "a mais recente primeiro");

        var pagina2 = await JsonAsync(await admin.GetAsync("/painel/whatsapp/conversas?pagina=2"));
        pagina2.GetProperty("itens").GetArrayLength().Should().Be(5);
        pagina2.GetProperty("pagina").GetInt32().Should().Be(2);
        pagina2.GetProperty("itens")[4].GetProperty("id").GetGuid().Should().Be(ids[0]);

        var aguardando = await JsonAsync(await admin.GetAsync("/painel/whatsapp/conversas?filtro=aguardando"));
        aguardando.GetProperty("total").GetInt32().Should().Be(1);
        aguardando.GetProperty("itens")[0].GetProperty("id").GetGuid().Should().Be(ids[3]);
        aguardando.GetProperty("itens")[0].GetProperty("aguardandoAtendimento").GetBoolean().Should().BeTrue();
        aguardando.GetProperty("itens")[0].GetProperty("humanoMotivo").GetString().Should().Be("PedidoDoAgente");

        (await JsonAsync(await admin.GetAsync("/painel/whatsapp/conversas?filtro=humano"))).GetProperty("total").GetInt32().Should().Be(2);
        (await JsonAsync(await admin.GetAsync("/painel/whatsapp/conversas?filtro=todas&pagina=0"))).GetProperty("pagina").GetInt32().Should().Be(1);

        var invalido = await admin.GetAsync("/painel/whatsapp/conversas?filtro=qualquer");
        invalido.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalido.Content.ReadAsStringAsync()).Should().Contain("filtro_invalido");

        var resumo = await JsonAsync(await admin.GetAsync("/painel/whatsapp/conversas/resumo"));
        resumo.GetProperty("aguardandoAtendimento").GetInt32().Should().Be(1);
        resumo.GetProperty("emAtendimentoHumano").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Mensagens_desde_humano_contam_so_o_que_o_cliente_escreveu_depois_de_assumir()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = await ConversaAsync(c);

        (await admin.PostAsync($"/painel/whatsapp/conversas/{conversa.Id}/assumir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        foreach (var texto in new[] { "alguém aí?", "quero falar com o dono", "obrigado" })
            await _ambiente.EnviarAsync(c, texto);

        var detalhe = await JsonAsync(await admin.GetAsync($"/painel/whatsapp/conversas/{conversa.Id}"));
        detalhe.GetProperty("status").GetString().Should().Be("Humano");
        detalhe.GetProperty("humanoMotivo").GetString().Should().Be("Painel");
        detalhe.GetProperty("mensagensDesdeHumano").GetInt32().Should().Be(3);
        detalhe.GetProperty("aguardandoAtendimento").GetBoolean().Should().BeFalse("quem assumiu pelo painel já atende");

        var lista = await JsonAsync(await admin.GetAsync("/painel/whatsapp/conversas?filtro=humano"));
        lista.GetProperty("itens")[0].GetProperty("mensagensDesdeHumano").GetInt32().Should().Be(3);
        (await JsonAsync(await admin.GetAsync("/painel/whatsapp/conversas/resumo"))).GetProperty("aguardandoAtendimento").GetInt32().Should().Be(0);
    }

    // ---------------------------------------------------------------- detalhe

    [Fact]
    public async Task Detalhe_nunca_mostra_papel_Ferramenta_nem_nota_de_contexto_e_mascara_o_link_de_gestao()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);
        await _ambiente.EnviarAsync(c, "oi, quero cortar");
        var conversa = await ConversaAsync(c);

        var agora = DateTimeOffset.UtcNow.AddMinutes(-1);
        await InserirMensagensAsync(c, conversa.Id, 1, PapelMensagemWhatsApp.Ferramenta, "listar_servicos", "FERRAMENTA-SECRETA", agora);
        await InserirMensagensAsync(c, conversa.Id, 1, PapelMensagemWhatsApp.Assistente, "contexto_consulta", "NOTA-INTERNA-COM-IDS", agora.AddSeconds(1));
        await InserirMensagensAsync(c, conversa.Id, 1, PapelMensagemWhatsApp.Sistema, "agendamento_resumo", "Resumo do servidor", agora.AddSeconds(2));
        await InserirMensagensAsync(
            c, conversa.Id, 1, PapelMensagemWhatsApp.Usuario, null, "https://x.agendeiiai.com.br/agendamentos/eyJabc.def.ghi cancela", agora.AddSeconds(3));

        var bruto = await (await admin.GetAsync($"/painel/whatsapp/conversas/{conversa.Id}")).Content.ReadAsStringAsync();
        bruto.Should().NotContain("FERRAMENTA-SECRETA").And.NotContain("NOTA-INTERNA-COM-IDS").And.NotContain("eyJabc");

        var detalhe = JsonDocument.Parse(bruto).RootElement;
        var mensagens = detalhe.GetProperty("mensagens").EnumerateArray().ToList();
        mensagens.Select(m => m.GetProperty("papel").GetString()).Distinct().Should().BeSubsetOf(["Cliente", "Assistente", "Sistema"]);
        mensagens.Select(m => m.GetProperty("texto").GetString()).Should().Contain(t => t!.StartsWith("Resumo do servidor")).And.Contain("oi, quero cortar");
        mensagens.Single(m => m.GetProperty("texto").GetString()!.Contains("cancela")).GetProperty("texto").GetString()
            .Should().Contain(Plataforma.Infraestrutura.WhatsAppAgente.MascaraLinkGestao.Marca);
        detalhe.GetProperty("haMaisAntigas").GetBoolean().Should().BeFalse();
        detalhe.GetProperty("telefone").GetString().Should().Be("+5571988887777");

        // Ordem cronológica.
        var datas = mensagens.Select(m => m.GetProperty("em").GetDateTimeOffset()).ToList();
        datas.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Detalhe_traz_ate_50_mensagens_em_ordem_e_carrega_as_anteriores_por_antesDe()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);
        var ids = await InserirConversasAsync(c, 1);
        await InserirMensagensAsync(c, ids[0], 120, prefixo: "m");

        var primeira = await JsonAsync(await admin.GetAsync($"/painel/whatsapp/conversas/{ids[0]}"));
        var lote1 = primeira.GetProperty("mensagens").EnumerateArray().ToList();
        lote1.Should().HaveCount(50);
        lote1[^1].GetProperty("texto").GetString().Should().Be("m-119", "as 50 mais recentes");
        lote1[0].GetProperty("texto").GetString().Should().Be("m-070");
        primeira.GetProperty("haMaisAntigas").GetBoolean().Should().BeTrue();

        var antesDe = Uri.EscapeDataString(lote1[0].GetProperty("em").GetDateTimeOffset().ToString("O"));
        var segunda = await JsonAsync(await admin.GetAsync($"/painel/whatsapp/conversas/{ids[0]}?antesDe={antesDe}"));
        var lote2 = segunda.GetProperty("mensagens").EnumerateArray().ToList();
        lote2.Should().HaveCount(50);
        lote2[^1].GetProperty("texto").GetString().Should().Be("m-069");
        lote2[0].GetProperty("texto").GetString().Should().Be("m-020");
        segunda.GetProperty("haMaisAntigas").GetBoolean().Should().BeTrue();

        var antesDe2 = Uri.EscapeDataString(lote2[0].GetProperty("em").GetDateTimeOffset().ToString("O"));
        var terceira = await JsonAsync(await admin.GetAsync($"/painel/whatsapp/conversas/{ids[0]}?antesDe={antesDe2}"));
        terceira.GetProperty("mensagens").GetArrayLength().Should().Be(20);
        terceira.GetProperty("haMaisAntigas").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Cursor_composto_percorre_130_mensagens_no_mesmo_instante_sem_omitir_nem_repetir()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);
        var ids = await InserirConversasAsync(c, 1);
        var instante = DateTimeOffset.UtcNow.AddHours(-3);
        await _ambiente.NoBancoAsync(async db =>
        {
            var instancia = await db.InstanciasWhatsApp.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.NegocioId == c.NegocioId);
            for (var i = 0; i < 130; i++)
            {
                var m = new MensagemWhatsApp(c.NegocioId, instancia.Id, ids[0], null, PapelMensagemWhatsApp.Usuario, $"igual-{i:D3}");
                db.Entry(m).Property(x => x.CriadoEm).CurrentValue = instante;
                db.MensagensWhatsApp.Add(m);
            }

            await db.SaveChangesAsync();
            return 0;
        });

        var vistos = new List<Guid>();
        string url = $"/painel/whatsapp/conversas/{ids[0]}";
        for (var volta = 0; volta < 10; volta++)
        {
            var pagina = await JsonAsync(await admin.GetAsync(url));
            var lote = pagina.GetProperty("mensagens").EnumerateArray().ToList();
            vistos.InsertRange(0, lote.Select(m => m.GetProperty("id").GetGuid()));
            if (!pagina.GetProperty("haMaisAntigas").GetBoolean())
                break;
            var primeira = lote[0];
            url = $"/painel/whatsapp/conversas/{ids[0]}?antesDe={Uri.EscapeDataString(primeira.GetProperty("em").GetDateTimeOffset().ToString("O"))}"
                + $"&antesDeId={primeira.GetProperty("id").GetGuid()}";
        }

        vistos.Should().HaveCount(130).And.OnlyHaveUniqueItems();
        var todas = await _ambiente.MensagensAsync();
        vistos.Should().BeEquivalentTo(todas.Select(m => m.Id));
    }

    [Fact]
    public async Task Pagina_enorme_devolve_lista_vazia_sem_estourar_o_deslocamento()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);
        await InserirConversasAsync(c, 3);

        foreach (var pagina in new[] { int.MaxValue, int.MaxValue - 1, 107_374_183 })
        {
            var resposta = await JsonAsync(await admin.GetAsync($"/painel/whatsapp/conversas?pagina={pagina}"));
            resposta.GetProperty("itens").GetArrayLength().Should().Be(0, pagina.ToString());
            resposta.GetProperty("total").GetInt32().Should().Be(3);
        }

        (await JsonAsync(await admin.GetAsync("/painel/whatsapp/conversas?pagina=1"))).GetProperty("itens").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Uso_aceita_de_2020_01_a_9999_11_e_recusa_alem_disso()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);

        (await admin.GetAsync("/painel/whatsapp/uso?mes=2020-01")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/painel/whatsapp/uso?mes=9999-11")).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var invalido in new[] { "9999-12", "2019-12", "10000-01" })
        {
            var resposta = await admin.GetAsync($"/painel/whatsapp/uso?mes={invalido}");
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, invalido);
            (await resposta.Content.ReadAsStringAsync()).Should().Contain("mes_invalido");
        }
    }

    // ---------------------------------------------------------------- apagar

    [Fact]
    public async Task Apagar_remove_as_mensagens_anonimiza_a_conversa_audita_sem_texto_e_depois_e_404()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);
        await _ambiente.EnviarAsync(c, SegredoTexto);
        var conversa = await ConversaAsync(c);
        (await _ambiente.MensagensAsync()).Should().NotBeEmpty();

        (await admin.PostAsync($"/painel/whatsapp/conversas/{conversa.Id}/apagar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _ambiente.MensagensAsync()).Should().BeEmpty();
        (await ConversaAsync(c, "anonimizado")).Anonimizada.Should().BeTrue();
        (await admin.GetAsync($"/painel/whatsapp/conversas/{conversa.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsync($"/painel/whatsapp/conversas/{conversa.Id}/apagar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var auditoria = await _ambiente.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.Acao == "ApagarConversaWhatsApp").ToListAsync());
        auditoria.Should().ContainSingle();
        auditoria[0].Detalhes.Should().BeNull();
    }

    // ---------------------------------------------------------------- uso de IA

    [Fact]
    public async Task Uso_agrega_o_mes_UTC_da_cota_e_isola_os_negocios()
    {
        var a = await _ambiente.SemearAsync();
        var b = await _ambiente.SemearAsync();
        var adminA = await LogarAsync(a);
        var adminB = await LogarAsync(b);
        var idsA = await InserirConversasAsync(a, 2);
        var idsB = await InserirConversasAsync(b, 1);

        var hoje = DateTimeOffset.UtcNow;
        var mesAtual = new DateTimeOffset(hoje.Year, hoje.Month, 1, 12, 0, 0, TimeSpan.Zero);
        var mesAnterior = mesAtual.AddMonths(-1);

        await _ambiente.NoBancoAsync(async db =>
        {
            void Uso(Guid negocio, Guid conversa, int entrada, int saida, bool ok, DateTimeOffset em)
            {
                var uso = new UsoIa(negocio, conversa, "modelo-falso", entrada, saida, ok);
                db.Entry(uso).Property(u => u.CriadoEm).CurrentValue = em;
                db.UsosIa.Add(uso);
            }

            Uso(a.NegocioId, idsA[0], 1000, 200, true, mesAtual);
            Uso(a.NegocioId, idsA[0], 500, 100, true, mesAtual.AddSeconds(1));
            Uso(a.NegocioId, idsA[1], 300, 0, false, mesAtual.AddDays(1).AddSeconds(5));
            Uso(a.NegocioId, idsA[1], 9999, 9999, true, mesAnterior); // outro mês: fora
            Uso(b.NegocioId, idsB[0], 7777, 1, true, mesAtual);       // outro negócio: fora
            Uso(a.NegocioId, idsA[1], 10, 5, true, hoje.AddSeconds(-2)); // "hoje" (pode cair no mesmo dia do mesmo mês)

            void Rodada(Guid negocio, Guid conversa, DesfechoRodadaAgente desfecho)
            {
                db.RodadasAgente.Add(new RodadaAgente(
                    Guid.NewGuid(), negocio, conversa, mesAtual, 100, desfecho, null, 1, 0, 0, null, null, null, null, null, null));
            }

            Rodada(a.NegocioId, idsA[0], DesfechoRodadaAgente.Respondida);
            Rodada(a.NegocioId, idsA[0], DesfechoRodadaAgente.Fallback);
            Rodada(a.NegocioId, idsA[1], DesfechoRodadaAgente.Fallback);
            Rodada(a.NegocioId, idsA[1], DesfechoRodadaAgente.Silencio);
            Rodada(a.NegocioId, idsA[1], DesfechoRodadaAgente.Transferida);
            Rodada(b.NegocioId, idsB[0], DesfechoRodadaAgente.Fallback);
            await db.SaveChangesAsync();
            return 0;
        });

        var uso = await JsonAsync(await adminA.GetAsync($"/painel/whatsapp/uso?mes={mesAtual:yyyy-MM}"));
        uso.GetProperty("mes").GetString().Should().Be(mesAtual.ToString("yyyy-MM"));
        uso.GetProperty("chamadas").GetInt32().Should().Be(4);
        uso.GetProperty("chamadasComFalha").GetInt32().Should().Be(1);
        uso.GetProperty("tokensEntrada").GetInt64().Should().Be(1000 + 500 + 300 + 10);
        uso.GetProperty("tokensSaida").GetInt64().Should().Be(200 + 100 + 0 + 5);
        uso.GetProperty("cotaMensalTokens").GetInt64().Should().Be(1_500_000);
        uso.GetProperty("percentualCota").GetDecimal().Should().Be(0.1m, "2115 de 1.500.000 tokens, uma casa");
        uso.GetProperty("limiteChamadasPorDia").GetInt32().Should().Be(250);
        uso.GetProperty("chamadasHoje").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        uso.GetProperty("conversasAtendidas").GetInt32().Should().Be(2, "duas conversas com rodada que não foi só silêncio");
        uso.GetProperty("respostasFallback").GetInt32().Should().Be(2);
        uso.GetProperty("transferenciasHumano").GetInt32().Should().Be(0, "vem da auditoria AtenderConversaPeloAgente");
        uso.GetProperty("cancelamentos").GetInt32().Should().Be(0);
        uso.GetProperty("remarcacoes").GetInt32().Should().Be(0);
        uso.GetProperty("agendamentosConfirmados").GetInt32().Should().Be(0);

        var dias = uso.GetProperty("porDia").EnumerateArray().ToList();
        dias.Select(d => d.GetProperty("dia").GetString()).Should().BeInAscendingOrder();
        dias.Sum(d => d.GetProperty("chamadas").GetInt32()).Should().Be(4);
        dias.Sum(d => d.GetProperty("tokens").GetInt64()).Should().Be(1000 + 200 + 500 + 100 + 300 + 15);
        dias.Single(d => d.GetProperty("dia").GetString() == mesAtual.ToString("yyyy-MM-dd") + "").GetProperty("chamadas").GetInt32()
            .Should().BeGreaterThanOrEqualTo(2);

        // O mês anterior é só dele; o outro negócio só enxerga o próprio.
        var anterior = await JsonAsync(await adminA.GetAsync($"/painel/whatsapp/uso?mes={mesAnterior:yyyy-MM}"));
        anterior.GetProperty("chamadas").GetInt32().Should().Be(1);
        anterior.GetProperty("tokensEntrada").GetInt64().Should().Be(9999);
        var deB = await JsonAsync(await adminB.GetAsync($"/painel/whatsapp/uso?mes={mesAtual:yyyy-MM}"));
        deB.GetProperty("chamadas").GetInt32().Should().Be(1);
        deB.GetProperty("tokensEntrada").GetInt64().Should().Be(7777);
        deB.GetProperty("respostasFallback").GetInt32().Should().Be(1);

        // Sem o parâmetro vale o mês corrente.
        (await JsonAsync(await adminA.GetAsync("/painel/whatsapp/uso"))).GetProperty("mes").GetString().Should().Be(hoje.ToString("yyyy-MM"));

        foreach (var invalido in new[] { "2026-13", "abc", "2026-1", "202610", "1999-01" })
        {
            var resposta = await adminA.GetAsync($"/painel/whatsapp/uso?mes={invalido}");
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, invalido);
            (await resposta.Content.ReadAsStringAsync()).Should().Contain("mes_invalido");
        }
    }

    [Fact]
    public async Task Uso_conta_agendamentos_confirmados_e_transferencias_de_verdade()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);

        var erro = await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria"));
        erro.Should().BeNull();
        await _ambiente.EnviarAsync(c, "sim");
        (await _ambiente.PendenciasAsync()).Should().ContainSingle(p => p.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada);

        var conversa = await ConversaAsync(c);
        (await admin.PostAsync($"/painel/whatsapp/conversas/{conversa.Id}/assumir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var uso = await JsonAsync(await admin.GetAsync("/painel/whatsapp/uso"));
        uso.GetProperty("agendamentosConfirmados").GetInt32().Should().Be(1);
        uso.GetProperty("chamadas").GetInt32().Should().BeGreaterThan(0);
    }

    // ---------------------------------------------------------------- status estendido e mensagem de transferência

    [Fact]
    public async Task Status_traz_os_campos_novos_conforme_as_flags_e_a_contratacao()
    {
        // Humano ligado, agendamento em Todos, negócio contratado.
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Humano:FerramentaAtiva"] = "true" });
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE instancias_whatsapp SET mensagem_transferencia = 'Atendemos até as 18h'");
            return 0;
        });

        var status = await JsonAsync(await admin.GetAsync("/painel/whatsapp/status"));
        status.GetProperty("agenteAtivo").GetBoolean().Should().BeTrue();
        status.GetProperty("agenteIaContratado").GetBoolean().Should().BeTrue();
        status.GetProperty("mensagemTransferencia").GetString().Should().Be("Atendemos até as 18h");
        status.GetProperty("iaAtiva").GetBoolean().Should().BeTrue();
        status.GetProperty("agendamentoAtivo").GetBoolean().Should().BeTrue();
        status.GetProperty("gestaoModo").GetString().Should().Be("Desligado");
        status.GetProperty("atendimentoHumanoAtivo").GetBoolean().Should().BeTrue();

        // Sem contratação nada fica ativo para o negócio, mesmo com as flags globais ligadas.
        await _ambiente.ContratarAgenteIaAsync(c.NegocioId, false);
        var semServico = await JsonAsync(await admin.GetAsync("/painel/whatsapp/status"));
        semServico.GetProperty("agenteIaContratado").GetBoolean().Should().BeFalse();
        semServico.GetProperty("iaAtiva").GetBoolean().Should().BeFalse();
        semServico.GetProperty("agendamentoAtivo").GetBoolean().Should().BeFalse();
        semServico.GetProperty("atendimentoHumanoAtivo").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Status_com_flags_no_padrao_mostra_humano_e_agendamento_desligados()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:Agendamento:Modo"] = "Desligado" });
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);

        var status = await JsonAsync(await admin.GetAsync("/painel/whatsapp/status"));
        status.GetProperty("iaAtiva").GetBoolean().Should().BeTrue();
        status.GetProperty("agendamentoAtivo").GetBoolean().Should().BeFalse();
        status.GetProperty("atendimentoHumanoAtivo").GetBoolean().Should().BeFalse();
        status.GetProperty("mensagemTransferencia").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Mensagem_de_transferencia_e_gravada_validada_e_auditada_sem_o_conteudo()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);

        (await admin.PutAsJsonAsync("/painel/whatsapp/mensagem-transferencia", new { mensagem = "  Respondemos em até 1h.\r\nObrigado!  " }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var salva = await _ambiente.NoBancoAsync(db => db.InstanciasWhatsApp.IgnoreQueryFilters().AsNoTracking().Select(i => i.MensagemTransferencia).SingleAsync());
        salva.Should().Be("Respondemos em até 1h.\nObrigado!");

        var longa = await admin.PutAsJsonAsync("/painel/whatsapp/mensagem-transferencia", new { mensagem = new string('a', 301) });
        longa.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await longa.Content.ReadAsStringAsync()).Should().Contain("mensagem_invalida");
        (await admin.PutAsJsonAsync("/painel/whatsapp/mensagem-transferencia", new { mensagem = "ok\u0007" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync("/painel/whatsapp/mensagem-transferencia", new { mensagem = new string('b', 300) })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await admin.PutAsJsonAsync("/painel/whatsapp/mensagem-transferencia", new { mensagem = (string?)null })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _ambiente.NoBancoAsync(db => db.InstanciasWhatsApp.IgnoreQueryFilters().AsNoTracking().Select(i => i.MensagemTransferencia).SingleAsync()))
            .Should().BeNull();

        var auditoria = await _ambiente.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.Acao == "AlterarMensagemTransferenciaWhatsApp").ToListAsync());
        auditoria.Should().HaveCount(3);
        auditoria.Should().OnlyContain(l => l.Detalhes != null && !l.Detalhes.Contains("Respondemos") && !l.Detalhes.Contains("bbb"));
    }

    [Fact]
    public async Task Mensagem_de_transferencia_sem_instancia_e_404_e_nao_vaza_para_outro_negocio()
    {
        var a = await _ambiente.SemearAsync();
        var b = await _ambiente.SemearAsync();
        var adminA = await LogarAsync(a);
        var adminB = await LogarAsync(b);
        await adminA.PutAsJsonAsync("/painel/whatsapp/mensagem-transferencia", new { mensagem = "só do A" });

        (await _ambiente.NoBancoAsync(db => db.InstanciasWhatsApp.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.NegocioId == b.NegocioId)))
            .MensagemTransferencia.Should().BeNull();
        (await JsonAsync(await adminB.GetAsync("/painel/whatsapp/status"))).GetProperty("mensagemTransferencia").ValueKind.Should().Be(JsonValueKind.Null);

        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM instancias_whatsapp WHERE negocio_id = {b.NegocioId}");
            return 0;
        });
        (await adminB.PutAsJsonAsync("/painel/whatsapp/mensagem-transferencia", new { mensagem = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- agenda

    [Fact]
    public async Task Agenda_mostra_origem_e_o_prazo_da_reserva_do_agente_que_espera_o_sim()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await LogarAsync(c);
        var dia = Dia();

        (await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria"))).Should().BeNull();
        var lista = (await admin.GetFromJsonAsync<List<AgendamentoResumo>>($"/painel/agenda?profissionalId={c.Ana}&data={dia:yyyy-MM-dd}"))!;
        var reserva = lista.Should().ContainSingle().Subject;
        reserva.Status.Should().Be("Reservado");
        reserva.Origem.Should().Be("WhatsApp");
        reserva.ReservadoAte.Should().NotBeNull().And.BeAfter(DateTimeOffset.UtcNow);

        await _ambiente.EnviarAsync(c, "sim");
        var depois = (await admin.GetFromJsonAsync<List<AgendamentoResumo>>($"/painel/agenda?profissionalId={c.Ana}&data={dia:yyyy-MM-dd}"))!;
        var confirmado = depois.Should().ContainSingle().Subject;
        confirmado.Status.Should().Be("Agendado");
        confirmado.Origem.Should().Be("WhatsApp");
        confirmado.ReservadoAte.Should().BeNull("depois de confirmado não há mais prazo");
    }
}
