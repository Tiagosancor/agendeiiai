using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.WhatsAppAgente.Ia;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.WhatsAppAgente.HelpersAgendamentoAgente;
using static Plataforma.Testes.Integracao.WhatsAppAgente.ProvedorIaRoteirizado;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Fase 4, Fatia 6: gestão no modo Link (<c>listar_meus_agendamentos</c> e <c>enviar_link_gestao</c>). Escopo por negócio e telefone,
/// 9º dígito, origens verificadas (P2), cliente excluído, sem dado do cadastro na IA, token fora do banco e da IA, modo desligado e
/// serviço premium. Sempre com o modelo falso roteirizado — nunca o Gemini real.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class GestaoLinkAgenteTestes : IAsyncLifetime
{
    private const string Tel2 = "5571988880002";
    private const string NomeCadastro = "Maria Cadastro Secreta";
    private const string EmailCadastro = "maria.secreta@cadastro.com";

    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public GestaoLinkAgenteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

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
            ["Ai:Gestao:Modo"] = "Link",
            ["Ai:LimiteMensagensPorTelefonePorHora"] = "1000",
            ["Ai:LimiteMensagensPorTelefonePorDia"] = "5000",
            ["Ai:MaxChamadasFerramentaPorIteracao"] = "10",
        };
        foreach (var (chave, valor) in extra ?? new Dictionary<string, string?>())
            config[chave] = valor;
        await _ambiente.IniciarAsync(config, capturarLogs: logs);
    }

    // ---------------------------------------------------------------- apoio

    private Task<Guid> AgendarAsync(
        CenarioAgente c, string telefone, OrigemAgendamento origem, DateTimeOffset inicio, Guid? profissional = null,
        Action<Agendamento>? ajustar = null) =>
        _ambiente.Fabrica.NoBancoAsync(async db =>
        {
            var tel = TelefoneE164.Criar(telefone);
            var cliente = await db.Clientes.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.NegocioId == c.NegocioId && x.Telefone == tel);
            if (cliente is null)
            {
                cliente = Cliente.Criar(c.NegocioId, NomeCadastro, tel, OrigemCliente.Painel, EmailCadastro);
                db.Clientes.Add(cliente);
                await db.SaveChangesAsync();
            }

            var agendamento = Agendamento.CriarConfirmado(
                c.NegocioId, profissional ?? c.Ana, cliente.Id, inicio, [new ItemServicoAgendamento(c.Corte, "Corte", 50m, 30)], origem: origem);
            ajustar?.Invoke(agendamento);
            db.Agendamentos.Add(agendamento);
            await db.SaveChangesAsync();
            return agendamento.Id;
        });

    /// <summary>O modelo pede as ferramentas na ordem dada (uma por chamada) e depois só responde texto.</summary>
    private int Roteirizar(params (string Nome, string Argumentos)[] pedidos)
    {
        var antes = _ambiente.Provedor.Chamadas;
        _ambiente.Provedor.Roteiro = (_, n) =>
            n - antes >= 1 && n - antes <= pedidos.Length ? Chama(pedidos[n - antes - 1]) : Texto("Certo!");
        return antes;
    }

    private async Task<JsonElement> ResultadoAsync(int antes, int indiceRequisicao = 1)
    {
        await Task.CompletedTask;
        return Resultados(_ambiente.Provedor.Requisicoes[antes + indiceRequisicao]).Last().Conteudo;
    }

    /// <summary>Textos entregues ao cliente que levam um link de gestão (a resposta de texto do modelo, "Certo!", não conta).</summary>
    private List<string> Links() => _ambiente.Evolution.Envios.Select(e => e.Texto).Where(t => t.Contains("/agendamentos/")).ToList();

    private static string TokenDoTexto(string texto)
    {
        var m = Regex.Match(texto, @"/agendamentos/(\S+)");
        m.Success.Should().BeTrue("o texto enviado deveria ter o link de gestão");
        return m.Groups[1].Value;
    }

    private static JsonElement Json(string texto) => JsonDocument.Parse(texto).RootElement.Clone();

    private async Task<JsonElement> GestaoPublicaAsync(CenarioAgente c, string token)
    {
        using var cliente = _ambiente.ClientePublico(c.Slug);
        var resposta = await cliente.GetAsync($"/publico/meus-agendamentos/{token}");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, "o link enviado precisa abrir");
        return Json(await resposta.Content.ReadAsStringAsync());
    }

    private string TokenDe(CenarioAgente c, Guid agendamentoId)
    {
        using var escopo = _ambiente.Fabrica.Services.CreateScope();
        return escopo.ServiceProvider.GetRequiredService<IServicoTokenPublico>().GerarTokenAgendamento(c.NegocioId, agendamentoId);
    }

    // ---------------------------------------------------------------- listar_meus_agendamentos

    [Fact]
    public async Task Lista_so_os_agendamentos_do_numero_de_origem_verificada_e_bate_com_as_acoes_do_link()
    {
        var c = await _ambiente.SemearAsync();
        var outro = await _ambiente.SemearAsync("Outro negócio");
        var perto = await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, DateTimeOffset.UtcNow.AddHours(1), c.Beto);
        var a = await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));
        var b = await AgendarAsync(c, Tel, OrigemAgendamento.WhatsApp, H(11, 0));

        // Nada disto pode aparecer para o número da conversa.
        await AgendarAsync(c, Tel, OrigemAgendamento.Painel, H(14, 0));
        await AgendarAsync(c, Tel, OrigemAgendamento.Encaixe, H(15, 0));
        await AgendarAsync(c, Tel2, OrigemAgendamento.LinkPublico, H(16, 0));
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(17, 0), ajustar: x => x.Cancelar());
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(9, 0), c.Beto, x => x.IniciarAtendimento());
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, DateTimeOffset.UtcNow.AddDays(-2));
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, DateTimeOffset.UtcNow.AddDays(90));
        await AgendarAsync(outro, Tel, OrigemAgendamento.LinkPublico, H(10, 0));

        var antes = Roteirizar(("listar_meus_agendamentos", "{}"));
        (await _ambiente.EnviarAsync(c, "quais são meus agendamentos?")).StatusCode.Should().Be(HttpStatusCode.OK);

        var resultado = await ResultadoAsync(antes);
        resultado.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("agendamentos", "truncado");
        resultado.GetProperty("truncado").GetBoolean().Should().BeFalse();
        var itens = resultado.GetProperty("agendamentos").EnumerateArray().ToList();
        itens.Select(i => i.GetProperty("id").GetGuid()).Should().Equal(perto, a, b);

        foreach (var item in itens)
        {
            item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
                "id", "servicos", "profissional", "data", "dia_semana", "hora", "pode_cancelar", "pode_remarcar", "motivo");
            // Mesma decisão do GET /publico/meus-agendamentos/{token} do mesmo agendamento.
            var publico = await GestaoPublicaAsync(c, TokenDe(c, item.GetProperty("id").GetGuid()));
            var acoes = publico.GetProperty("acoes");
            item.GetProperty("pode_cancelar").GetBoolean().Should().Be(acoes.GetProperty("cancelar").GetProperty("permitido").GetBoolean());
            item.GetProperty("pode_remarcar").GetBoolean().Should().Be(acoes.GetProperty("remarcar").GetProperty("permitido").GetBoolean());
        }

        itens[0].GetProperty("pode_cancelar").GetBoolean().Should().BeFalse();
        itens[0].GetProperty("pode_remarcar").GetBoolean().Should().BeFalse();
        itens[0].GetProperty("motivo").GetString().Should().Be("antecedencia_minima");
        itens[0].GetProperty("profissional").GetString().Should().Be("Beto");
        itens[1].GetProperty("pode_cancelar").GetBoolean().Should().BeTrue();
        itens[1].GetProperty("motivo").ValueKind.Should().Be(JsonValueKind.Null);
        itens[1].GetProperty("servicos").EnumerateArray().Select(s => s.GetString()).Should().Equal("Corte");
        itens[1].GetProperty("data").GetString().Should().Be(Dia().ToString("yyyy-MM-dd"));
        itens[1].GetProperty("dia_semana").GetString().Should().Be("segunda-feira");
        itens[1].GetProperty("hora").GetString().Should().Be("10:00");
    }

    [Fact]
    public async Task Lista_pelas_duas_formas_do_nono_digito()
    {
        var c = await _ambiente.SemearAsync();
        // Cadastro antigo, sem o 9º dígito; a conversa chega com o 9.
        var id = await AgendarAsync(c, "557188887777", OrigemAgendamento.LinkPublico, H(10, 0));

        var antes = Roteirizar(("listar_meus_agendamentos", "{}"));
        await _ambiente.EnviarAsync(c, "meus horários");

        (await ResultadoAsync(antes)).GetProperty("agendamentos").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(id);
    }

    [Fact]
    public async Task Cliente_excluido_nao_aparece()
    {
        var c = await _ambiente.SemearAsync();
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE clientes SET excluido = true");
            return 0;
        });

        var antes = Roteirizar(("listar_meus_agendamentos", "{}"));
        await _ambiente.EnviarAsync(c, "meus horários");

        (await ResultadoAsync(antes)).GetProperty("agendamentos").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Limita_a_cinco_e_marca_truncado()
    {
        var c = await _ambiente.SemearAsync();
        for (var i = 0; i < 7; i++)
            await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(9, 0, Dia()).AddDays(7 * i));

        var antes = Roteirizar(("listar_meus_agendamentos", "{}"));
        await _ambiente.EnviarAsync(c, "meus horários");

        var resultado = await ResultadoAsync(antes);
        resultado.GetProperty("agendamentos").GetArrayLength().Should().Be(5);
        resultado.GetProperty("truncado").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Argumentos_extras_do_modelo_sao_recusados_e_o_telefone_e_sempre_o_da_conversa()
    {
        var c = await _ambiente.SemearAsync();
        await AgendarAsync(c, Tel2, OrigemAgendamento.LinkPublico, H(10, 0));

        var antes = Roteirizar(("listar_meus_agendamentos", $$"""{"telefone":"{{Tel2}}"}"""));
        await _ambiente.EnviarAsync(c, "agendamentos do 5571988880002");

        (await ResultadoAsync(antes)).GetProperty("erro").GetString().Should().Be("argumentos_invalidos");
    }

    [Fact]
    public async Task A_IA_nunca_recebe_dado_do_cadastro_do_cliente()
    {
        var c = await _ambiente.SemearAsync();
        await AgendarAsync(c, Tel, OrigemAgendamento.WhatsApp, H(10, 0));

        var antes = Roteirizar(("listar_meus_agendamentos", "{}"));
        await _ambiente.EnviarAsync(c, "meus agendamentos");
        _ambiente.Provedor.Chamadas.Should().Be(antes + 2);

        foreach (var requisicao in _ambiente.Provedor.Requisicoes)
        {
            var texto = Serializar(requisicao);
            texto.Should().NotContain(NomeCadastro).And.NotContain(EmailCadastro).And.NotContain("maria").And.NotContain("988887777")
                .And.NotContain("5571988887777");
        }
    }

    // ---------------------------------------------------------------- enviar_link_gestao

    [Fact]
    public async Task Envia_o_link_do_unico_agendamento_o_link_abre_e_o_token_nao_vai_ao_banco_nem_a_IA()
    {
        await IniciarAsync(logs: true);
        var c = await _ambiente.SemearAsync();
        var id = await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));

        var antes = _ambiente.Provedor.Chamadas;
        _ambiente.Provedor.Roteiro = (_, n) => n == antes + 1
            ? Chama(("enviar_link_gestao", "{}"))
            : Texto("Cancelei o seu horário! Acesse https://golpe.com/pix");
        await _ambiente.EnviarAsync(c, "quero cancelar meu horário");

        // A rodada termina na ferramenta: o texto do modelo, se existisse, é descartado.
        _ambiente.Provedor.Chamadas.Should().Be(antes + 1);
        var envio = _ambiente.Evolution.Envios.Single();
        envio.Texto.Should().Contain("Aqui está o link").And.Contain("Corte").And.NotContain("golpe").And.NotContain("Cancelei");
        var token = TokenDoTexto(envio.Texto);
        var linhasDeLog = _ambiente.Logs!.Linhas;

        var publico = await GestaoPublicaAsync(c, token);
        publico.GetProperty("id").GetGuid().Should().Be(id);

        foreach (var mensagem in await _ambiente.MensagensAsync())
            (mensagem.Conteudo ?? string.Empty).Should().NotContain(token).And.NotContain("/agendamentos/");
        var marcadas = await _ambiente.MarcadoresAsync("gestao_link");
        marcadas.Should().ContainSingle().Which.Conteudo.Should().Contain("[link de gestão enviado]");

        foreach (var requisicao in _ambiente.Provedor.Requisicoes)
            Serializar(requisicao).Should().NotContain(token).And.NotContain("/agendamentos/");
        linhasDeLog.Should().NotContain(l => l.Contains(token) || l.Contains("988887777"));
    }

    [Fact]
    public async Task Com_mais_de_um_exige_escolha_e_depois_envia_o_link_do_escolhido()
    {
        var c = await _ambiente.SemearAsync();
        var a = await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));
        var b = await AgendarAsync(c, Tel, OrigemAgendamento.WhatsApp, H(11, 0));

        var antes = Roteirizar(("enviar_link_gestao", "{}"));
        await _ambiente.EnviarAsync(c, "quero cancelar");

        var primeiro = await ResultadoAsync(antes);
        primeiro.GetProperty("erro").GetString().Should().Be("escolha_necessaria");
        var opcoes = primeiro.GetProperty("agendamentos").EnumerateArray().ToList();
        opcoes.Select(o => o.GetProperty("id").GetGuid()).Should().Equal(a, b);
        opcoes.Should().OnlyContain(o => o.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "id", "data", "hora" }));

        Links().Should().BeEmpty("a escolha volta ao cliente; ainda não há link");

        // A escolha consumiu a ferramenta de escrita desta rodada: o cliente responde e a rodada seguinte envia o link do escolhido.
        Roteirizar(("enviar_link_gestao", $$"""{"agendamento_id":"{{b}}"}"""));
        await _ambiente.EnviarAsync(c, "o das 11");

        (await GestaoPublicaAsync(c, TokenDoTexto(Links().Single()))).GetProperty("id").GetGuid().Should().Be(b);
    }

    [Fact]
    public async Task Agendamento_de_outro_numero_negocio_origem_ou_inexistente_da_a_mesma_resposta_e_nada_e_enviado()
    {
        var c = await _ambiente.SemearAsync();
        var outro = await _ambiente.SemearAsync("Outro negócio");
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));
        var doOutroNumero = await AgendarAsync(c, Tel2, OrigemAgendamento.LinkPublico, H(11, 0));
        var doOutroNegocio = await AgendarAsync(outro, Tel, OrigemAgendamento.LinkPublico, H(10, 0));
        var painel = await AgendarAsync(c, Tel, OrigemAgendamento.Painel, H(14, 0));
        var encaixe = await AgendarAsync(c, Tel, OrigemAgendamento.Encaixe, H(15, 0));
        var cancelado = await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(16, 0), ajustar: x => x.Cancelar());

        var esperado = (string?)null;
        foreach (var alvo in new[] { doOutroNumero, doOutroNegocio, painel, encaixe, cancelado, Guid.NewGuid() })
        {
            var antes = Roteirizar(("enviar_link_gestao", $$"""{"agendamento_id":"{{alvo}}"}"""));
            await _ambiente.EnviarAsync(c, "cancela esse");

            var resultado = await ResultadoAsync(antes);
            resultado.GetProperty("erro").GetString().Should().Be("agendamento_desconhecido", alvo.ToString());
            esperado ??= resultado.GetRawText();
            resultado.GetRawText().Should().Be(esperado, "sem oráculo: id alheio e inexistente respondem igual");
        }

        Links().Should().BeEmpty("nenhum link foi enviado");
        (await _ambiente.MarcadoresAsync("gestao_link")).Should().BeEmpty();
    }

    [Fact]
    public async Task Sem_agendamento_elegivel_responde_desconhecido()
    {
        var c = await _ambiente.SemearAsync();
        await AgendarAsync(c, Tel, OrigemAgendamento.Painel, H(10, 0));

        var antes = Roteirizar(("enviar_link_gestao", "{}"));
        await _ambiente.EnviarAsync(c, "quero cancelar");

        (await ResultadoAsync(antes)).GetProperty("erro").GetString().Should().Be("agendamento_desconhecido");
    }

    [Theory]
    [InlineData("""{"telefone":"5571988880002"}""")]
    [InlineData("""{"token":"abc"}""")]
    [InlineData("""{"negocio_id":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"agendamento_id":"nao-e-uuid"}""")]
    [InlineData("""{"agendamento_id":123}""")]
    [InlineData("""[]""")]
    public async Task Argumentos_invalidos_nao_enviam_nada(string argumentos)
    {
        var c = await _ambiente.SemearAsync();
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));

        var antes = Roteirizar(("enviar_link_gestao", argumentos));
        await _ambiente.EnviarAsync(c, "quero cancelar");

        (await ResultadoAsync(antes)).GetProperty("erro").GetString().Should().Be("argumentos_invalidos");
        Links().Should().BeEmpty();
    }

    [Fact]
    public async Task Limite_diario_de_links_por_telefone()
    {
        await IniciarAsync(new() { ["Ai:Gestao:LimiteLinksPorTelefonePorDia"] = "1" });
        var c = await _ambiente.SemearAsync();
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));

        Roteirizar(("enviar_link_gestao", "{}"));
        await _ambiente.EnviarAsync(c, "quero cancelar");
        Links().Should().ContainSingle();

        var antes = Roteirizar(("enviar_link_gestao", "{}"));
        await _ambiente.EnviarAsync(c, "manda de novo");

        (await ResultadoAsync(antes)).GetProperty("erro").GetString().Should().Be("limite_gestao");
        Links().Should().ContainSingle("o 2º link foi barrado");
    }

    [Fact]
    public async Task Agendamento_que_deixa_de_ser_elegivel_entre_o_pedido_e_o_envio_nao_gera_token()
    {
        var c = await _ambiente.SemearAsync();
        var id = await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));
        Roteirizar();
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = (await _ambiente.ConversasAsync()).Single();

        // O painel cancela depois de a ferramenta aceitar e antes de o servidor montar o texto.
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE agendamentos SET status = 'Cancelado' WHERE id = {id}");
            return 0;
        });

        using var escopo = _ambiente.Fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(c.NegocioId);
        var mensagem = await escopo.ServiceProvider.GetRequiredService<Plataforma.Infraestrutura.WhatsAppAgente.EmissorLinkGestaoAgente>()
            .MontarAsync(conversa.Id, id, c.Slug, null, CancellationToken.None);

        mensagem.Texto.Should().NotContain("/agendamentos/").And.Contain("Não consegui encontrar");
        mensagem.Marcador.Should().Be("gestao_nao_enviada", "não conta no limite diário de links");
    }

    // ---------------------------------------------------------------- flags, premium e prompt

    [Fact]
    public async Task Modo_Link_oferece_as_duas_ferramentas_e_usa_o_prompt_v4()
    {
        var c = await _ambiente.SemearAsync();

        Roteirizar();
        await _ambiente.EnviarAsync(c, "oi");

        var pedido = _ambiente.Provedor.Requisicoes.Last();
        pedido.Ferramentas.Select(f => f.Nome).Should().Contain(["listar_meus_agendamentos", "enviar_link_gestao", "preparar_agendamento"]);
        pedido.PromptSistema.Should().Contain("prompt v4").And.Contain("listar_meus_agendamentos").And.Contain("enviar_link_gestao");

        // O esquema nunca deixa o modelo informar negócio, telefone, cliente ou token.
        foreach (var ferramenta in pedido.Ferramentas.Where(f => f.Nome is "listar_meus_agendamentos" or "enviar_link_gestao"))
        {
            var propriedades = ferramenta.EsquemaParametros.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
            propriedades.Should().OnlyContain(p => p == "agendamento_id");
            ferramenta.EsquemaParametros.GetRawText().Should().NotContain("telefone").And.NotContain("negocio").And.NotContain("cliente").And.NotContain("token");
        }
    }

    [Fact]
    public async Task Modo_Link_com_atendente_humano_mantem_as_duas_regras()
    {
        await IniciarAsync(new() { ["Ai:Humano:FerramentaAtiva"] = "true" });
        var c = await _ambiente.SemearAsync();

        Roteirizar();
        await _ambiente.EnviarAsync(c, "oi");

        var pedido = _ambiente.Provedor.Requisicoes.Last();
        pedido.Ferramentas.Select(f => f.Nome).Should().Contain(["listar_meus_agendamentos", "enviar_link_gestao", "chamar_atendente"]);
        pedido.PromptSistema.Should().Contain("prompt v4").And.Contain("chamar_atendente").And.Contain("enviar_link_gestao");
    }

    [Theory]
    [InlineData("Cancelar")]
    [InlineData("Completo")]
    public async Task Cancelar_e_Completo_se_comportam_como_Link_por_enquanto(string modo)
    {
        await IniciarAsync(new() { ["Ai:Gestao:Modo"] = modo });
        var c = await _ambiente.SemearAsync();
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));

        var antes = Roteirizar(("enviar_link_gestao", "{}"));
        await _ambiente.EnviarAsync(c, "quero cancelar");

        _ambiente.Provedor.Requisicoes[antes].Ferramentas.Select(f => f.Nome).Should().NotContain(n => n.StartsWith("preparar_cancel") || n.StartsWith("preparar_remarc"));
        _ambiente.Evolution.Envios.Should().ContainSingle().Which.Texto.Should().Contain("/agendamentos/");
    }

    [Fact]
    public async Task Modo_desligado_mantem_o_comportamento_anterior()
    {
        await IniciarAsync(new() { ["Ai:Gestao:Modo"] = "Desligado" });
        var c = await _ambiente.SemearAsync();
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));

        var antes = Roteirizar(("listar_meus_agendamentos", "{}"), ("enviar_link_gestao", "{}"));
        await _ambiente.EnviarAsync(c, "quero cancelar");

        var pedido = _ambiente.Provedor.Requisicoes[antes];
        pedido.Ferramentas.Select(f => f.Nome).Should().NotContain(["listar_meus_agendamentos", "enviar_link_gestao"]);
        pedido.PromptSistema.Should().Contain("prompt v2").And.NotContain("listar_meus_agendamentos").And.NotContain("prompt v4");
        (await ResultadoAsync(antes)).GetProperty("erro").GetString().Should().Be("ferramenta_desconhecida");
        (await ResultadoAsync(antes, 2)).GetProperty("erro").GetString().Should().Be("ferramenta_desconhecida");
        _ambiente.Evolution.Envios.Should().NotContain(e => e.Texto.Contains("/agendamentos/"));
    }

    [Fact]
    public async Task Sem_o_agendamento_pelo_agente_a_gestao_tambem_fica_desligada()
    {
        await IniciarAsync(new() { ["Ai:Agendamento:Modo"] = "Desligado" });
        var c = await _ambiente.SemearAsync();

        Roteirizar();
        await _ambiente.EnviarAsync(c, "oi");

        var pedido = _ambiente.Provedor.Requisicoes.Last();
        pedido.Ferramentas.Select(f => f.Nome).Should().NotContain(["listar_meus_agendamentos", "enviar_link_gestao"]);
        pedido.PromptSistema.Should().Contain("prompt v1");
    }

    [Fact]
    public async Task Sem_o_servico_premium_contratado_nada_roda()
    {
        var c = await _ambiente.SemearAsync(contratado: false);
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));

        Roteirizar(("enviar_link_gestao", "{}"));
        await _ambiente.EnviarAsync(c, "quero cancelar");

        _ambiente.Provedor.Chamadas.Should().Be(0);
        _ambiente.Evolution.Envios.Should().NotContain(e => e.Texto.Contains("/agendamentos/"));
        (await _ambiente.UsosAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Servico_desligado_no_meio_da_rodada_nao_envia_o_link()
    {
        var c = await _ambiente.SemearAsync();
        await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));

        // O modelo pede o link; o serviço é desligado enquanto ele "pensava" (antes de a ferramenta rodar).
        _ambiente.Provedor.Roteiro = (_, n) =>
        {
            if (n == 1)
                _ambiente.ContratarAgenteIaAsync(c.NegocioId, false).GetAwaiter().GetResult();
            return n == 1 ? Chama(("enviar_link_gestao", "{}")) : Texto("ok");
        };
        await _ambiente.EnviarAsync(c, "quero cancelar");

        _ambiente.Evolution.Envios.Should().BeEmpty("com o serviço desligado nada do modelo é enviado");
    }

    [Fact]
    public async Task Status_do_painel_mostra_o_modo_efetivo_de_gestao()
    {
        var c = await _ambiente.SemearAsync();
        var admin = await _ambiente.AdminAsync(c);
        var link = Json(await (await admin.GetAsync("/painel/whatsapp/status")).Content.ReadAsStringAsync());
        link.GetProperty("gestaoModo").GetString().Should().Be("Link");

        await IniciarAsync(new() { ["Ai:Gestao:Modo"] = "Completo" });
        var c2 = await _ambiente.SemearAsync();
        var admin2 = await _ambiente.AdminAsync(c2);
        Json(await (await admin2.GetAsync("/painel/whatsapp/status")).Content.ReadAsStringAsync())
            .GetProperty("gestaoModo").GetString().Should().Be("Link", "Cancelar/Completo ainda valem como Link");

        await IniciarAsync(new() { ["Ai:Gestao:Modo"] = "Desligado" });
        var c3 = await _ambiente.SemearAsync();
        var admin3 = await _ambiente.AdminAsync(c3);
        Json(await (await admin3.GetAsync("/painel/whatsapp/status")).Content.ReadAsStringAsync())
            .GetProperty("gestaoModo").GetString().Should().Be("Desligado");

        await IniciarAsync();
        var c4 = await _ambiente.SemearAsync(contratado: false);
        var admin4 = await _ambiente.AdminAsync(c4);
        Json(await (await admin4.GetAsync("/painel/whatsapp/status")).Content.ReadAsStringAsync())
            .GetProperty("gestaoModo").GetString().Should().Be("Desligado", "sem o serviço contratado nada fica ativo");
    }

    // ---------------------------------------------------------------- memória entre mensagens e guarda

    [Fact]
    public async Task A_nota_de_contexto_preserva_os_ids_dos_agendamentos_para_a_mensagem_seguinte()
    {
        var c = await _ambiente.SemearAsync();
        var a = await AgendarAsync(c, Tel, OrigemAgendamento.LinkPublico, H(10, 0));
        var b = await AgendarAsync(c, Tel, OrigemAgendamento.WhatsApp, H(11, 0));

        Roteirizar(("listar_meus_agendamentos", "{}"));
        await _ambiente.EnviarAsync(c, "quais meus horários?");

        Roteirizar();
        await _ambiente.EnviarAsync(c, "cancela o das 11");

        var historico = string.Join("\n", _ambiente.Provedor.Requisicoes.Last().Mensagens.Where(m => m.Papel == PapelIa.Modelo).Select(m => m.Texto));
        historico.Should().Contain(Plataforma.Infraestrutura.WhatsAppAgente.NotaContextoAgente.Rotulo).And.Contain("Agendamentos do cliente").And.Contain(a.ToString()).And.Contain(b.ToString());
        historico.Should().NotContain(NomeCadastro).And.NotContain("988887777");
    }

    [Fact]
    public async Task Modelo_que_diz_que_cancelou_recebe_o_aviso_fixo_e_o_link_de_gestao_dele_nao_passa()
    {
        var c = await _ambiente.SemearAsync();
        var url = $"http://{c.Slug}.{DominioDeTeste.Valor}/agendamentos/eyJabcdef.ghijkl.mnopqr";

        _ambiente.Provedor.Roteiro = (_, _) => Texto($"Pronto, cancelei o seu horário. Se quiser, acesse {url}");
        await _ambiente.EnviarAsync(c, "cancela meu horário");

        var texto = _ambiente.UltimaEntrega()!;
        texto.Should().Contain("não cancelei nem remarquei nada").And.NotContain("/agendamentos/").And.NotContain("eyJ");
    }
}
