using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plataforma.Api.Controllers.Publico;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Aplicacao.WhatsAppAgente.Ia;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Agente de consulta (Fase 2) com o "modelo" roteirizado: critério de aceite (horários idênticos aos do assistente público),
/// UsoIa, limites, assinatura, provedor Desligado, piloto, rodada única por conversa, fila real e recuperação.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AgenteConsultaTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public AgenteConsultaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(new Dictionary<string, string?>
        {
            ["Ai:LimiteMensagensPorTelefonePorHora"] = "3",
            ["Ai:LimiteMensagensPorTelefonePorDia"] = "50",
            ["Ai:LimiteChamadasPorNegocioPorDia"] = "250",
            ["Ai:CotaMensalTokensPorNegocio"] = "100000",
        });
    }

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    // ---------------------------------------------------------------- critério de aceite

    [Fact]
    public async Task Pergunta_de_horario_chama_as_tres_ferramentas_e_a_lista_e_identica_a_do_assistente_publico()
    {
        var c = await _ambiente.SemearAsync();
        var dia = AmbienteAgente.ProximaSegunda().ToString("yyyy-MM-dd");

        _ambiente.Provedor.Roteiro = (requisicao, n) => n switch
        {
            1 => ProvedorIaRoteirizado.Chama(("listar_servicos", "{}")),
            2 => ProvedorIaRoteirizado.Chama(("listar_profissionais", $$"""{"servico_id":"{{c.Corte}}"}""")),
            3 => ProvedorIaRoteirizado.Chama(("consultar_horarios", $$"""{"servico_id":"{{c.Corte}}","profissional_id":"{{c.Ana}}","data":"{{dia}}"}""")),
            _ => ProvedorIaRoteirizado.Texto("Horários da Ana: " + string.Join(", ", HorasDoUltimoResultado(requisicao))),
        };

        (await _ambiente.EnviarAsync(c, "tem horário pra corte na segunda com a Ana?")).StatusCode.Should().Be(HttpStatusCode.OK);

        var esperado = await HorasPublicasAsync(c, $"data={dia}&duracaoMinutos=30&servicoIds={c.Corte}&profissionalId={c.Ana}");
        esperado.Should().NotBeEmpty();
        _ambiente.Provedor.Chamadas.Should().Be(4);
        _ambiente.UltimaEntrega().Should().Be("Horários da Ana: " + string.Join(", ", esperado));

        // A ferramenta devolveu exatamente as mesmas horas (fuso do negócio, HH:mm) do endpoint público.
        HorasDoUltimoResultado(_ambiente.Provedor.Requisicoes[^1]).Should().Equal(esperado);

        // Preço e duração dele (personalizados do vínculo) vieram da ferramenta de profissionais.
        var profissionais = ProvedorIaRoteirizado.Resultados(_ambiente.Provedor.Requisicoes[2]).Single(r => r.Nome == "listar_profissionais").Conteudo;
        profissionais.EnumerateArray().Select(p => (p.GetProperty("nome").GetString(), p.GetProperty("preco").GetString(), p.GetProperty("duracao_minutos").GetInt32()))
            .Should().BeEquivalentTo([("Ana", "R$ 70,00", 60), ("Beto", "R$ 50,00", 30)]);

        // Cada chamada ao modelo gravou o uso (só números).
        var usos = await _ambiente.UsosAsync();
        usos.Should().HaveCount(4).And.OnlyContain(u => u.Sucesso && u.TokensEntrada == 100 && u.TokensSaida == 20 && u.Modelo == "modelo-falso" && u.NegocioId == c.NegocioId);

        // Chamadas de ferramenta ficam salvas para auditoria (não reenviadas ao modelo).
        (await _ambiente.MensagensAsync()).Where(m => m.Papel == PapelMensagemWhatsApp.Ferramenta).Select(m => m.NomeFerramenta)
            .Should().Equal("listar_servicos", "listar_profissionais", "consultar_horarios");
    }

    [Fact]
    public async Task Qualquer_profissional_agrega_quem_faz_o_servico_e_trunca_em_30_como_o_publico_na_mesma_ordem()
    {
        var c = await _ambiente.SemearAsync();
        var dia = AmbienteAgente.ProximaSegunda().ToString("yyyy-MM-dd");

        _ambiente.Provedor.Roteiro = (requisicao, n) => n == 1
            ? ProvedorIaRoteirizado.Chama(("consultar_horarios", $$"""{"servico_id":"{{c.Corte}}","data":"{{dia}}"}"""))
            : ProvedorIaRoteirizado.Texto("ok");

        await _ambiente.EnviarAsync(c, "algum horário na segunda para corte?");

        using var cliente = _ambiente.ClientePublico(c.Slug);
        var publico = (await cliente.GetFromJsonAsync<List<HorarioLivrePublico>>(
            $"/publico/horarios-livres?data={dia}&duracaoMinutos=30&servicoIds={c.Corte}"))!;
        publico.Count.Should().BeGreaterThan(30, "Ana (60 min) e Beto (30 min) juntos passam do teto da ferramenta");

        var resultado = ProvedorIaRoteirizado.Resultados(_ambiente.Provedor.Requisicoes[1]).Single().Conteudo;
        resultado.GetProperty("truncado").GetBoolean().Should().BeTrue();
        resultado.GetProperty("dia_semana").GetString().Should().Be("segunda-feira");
        var itens = resultado.GetProperty("horarios").EnumerateArray().ToList();
        itens.Should().HaveCount(30);
        itens.Select(i => (i.GetProperty("hora").GetString(), i.GetProperty("profissional_id").GetGuid()))
            .Should().Equal(publico.Take(30).Select(h => (AmbienteAgente.HoraLocal(h.Inicio), h.ProfissionalId)));
        itens.Select(i => i.GetProperty("profissional").GetString()).Distinct().Should().BeSubsetOf(["Ana", "Beto"]);
    }

    // ---------------------------------------------------------------- isolamento entre negócios (A2)

    [Fact]
    public async Task Ids_de_outro_negocio_sao_desconhecidos_e_argumento_extra_de_negocio_e_recusado()
    {
        var a = await _ambiente.SemearAsync("Negócio Alfa", "Corte Alfa");
        var b = await _ambiente.SemearAsync("Negócio Beta", "Corte Beta");
        var dia = AmbienteAgente.ProximaSegunda().ToString("yyyy-MM-dd");

        _ambiente.Provedor.Roteiro = (_, n) => n switch
        {
            1 => ProvedorIaRoteirizado.Chama(
                ("consultar_horarios", $$"""{"servico_id":"{{b.Corte}}","data":"{{dia}}"}"""),
                ("consultar_horarios", $$"""{"servico_id":"{{a.Corte}}","profissional_id":"{{b.Ana}}","data":"{{dia}}"}"""),
                ("consultar_horarios", $$"""{"servico_id":"{{a.Corte}}","data":"{{dia}}","negocio_id":"{{b.NegocioId}}"}""")),
            2 => ProvedorIaRoteirizado.Chama(("listar_servicos", "{}")),
            _ => ProvedorIaRoteirizado.Texto("ok"),
        };

        await _ambiente.EnviarAsync(a, "horários?");

        var erros = ProvedorIaRoteirizado.Resultados(_ambiente.Provedor.Requisicoes[1]).Select(r => r.Conteudo.GetProperty("erro").GetString()).ToList();
        erros.Should().Equal("servico_desconhecido", "profissional_desconhecido", "argumentos_invalidos");

        // listar_servicos na conversa de A não enxerga nada de B.
        var servicos = ProvedorIaRoteirizado.Resultados(_ambiente.Provedor.Requisicoes[2]).Last(r => r.Nome == "listar_servicos").Conteudo;
        var ids = servicos.EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        ids.Should().Contain(a.Corte).And.NotContain([b.Corte, b.Barba, b.Tratamento]);
        servicos.EnumerateArray().Select(s => s.GetProperty("nome").GetString()).Should().NotContain("Corte Beta");
    }

    [Fact]
    public async Task Processador_chamado_sem_negocio_resolvido_nao_le_nada_de_conversa_alheia()
    {
        var c = await _ambiente.SemearAsync();

        using var escopo = _ambiente.Fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        // Sem IContextoNegocio.Definir, o filtro global é fail-closed: nenhuma linha, nunca "tudo".
        (await db.Servicos.ToListAsync()).Should().BeEmpty();
        (await db.InstanciasWhatsApp.ToListAsync()).Should().BeEmpty();
        (await db.MensagensWhatsApp.ToListAsync()).Should().BeEmpty();

        // E um processamento de conversa inexistente simplesmente não faz nada.
        await escopo.ServiceProvider.GetRequiredService<IProcessadorRodadaAgente>().ProcessarAsync(Guid.NewGuid());
        _ambiente.Provedor.Chamadas.Should().Be(0);
        c.NegocioId.Should().NotBeEmpty();
    }

    // ---------------------------------------------------------------- dados enviados à IA (A3)

    [Fact]
    public async Task Nada_pessoal_vai_para_a_IA_nem_o_telefone_nem_o_cadastro_do_cliente_nem_quem_ocupa_o_horario()
    {
        var c = await _ambiente.SemearAsync();
        var dia = AmbienteAgente.ProximaSegunda();
        var inicio = new DateTimeOffset(dia.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(-3));

        // Cliente cadastrado com o MESMO telefone de quem escreve e outro cliente agendado no dia consultado.
        await _ambiente.NoBancoAsync(async db =>
        {
            db.Clientes.Add(Plataforma.Dominio.Clientes.Cliente.Criar(c.NegocioId, "Fulano Sigiloso Silva", Plataforma.Dominio.Comum.TelefoneE164.Criar("+" + AmbienteAgente.TelefoneCliente)));
            return await db.SaveChangesAsync();
        });
        await _ambiente.Fabrica.SemearAgendamentoAsync(c.NegocioId, c.Ana, inicio, servicoId: c.Corte);
        await _ambiente.NoBancoAsync(async db =>
        {
            var outro = await db.Clientes.IgnoreQueryFilters().FirstAsync(x => x.Nome == "Cliente Sobrenome");
            outro.AtualizarDados("Beltrano Reservado Souza", "beltrano@exemplo.com", null);
            return await db.SaveChangesAsync();
        });

        _ambiente.Provedor.Roteiro = (_, n) => n switch
        {
            1 => ProvedorIaRoteirizado.Chama(("listar_servicos", "{}"), ("listar_profissionais", "{}")),
            2 => ProvedorIaRoteirizado.Chama(("consultar_horarios", $$"""{"servico_id":"{{c.Corte}}","profissional_id":"{{c.Ana}}","data":"{{dia:yyyy-MM-dd}}"}""")),
            _ => ProvedorIaRoteirizado.Texto("ok"),
        };

        await _ambiente.EnviarAsync(c, "Quero um corte");

        _ambiente.Provedor.Chamadas.Should().Be(3);
        foreach (var requisicao in _ambiente.Provedor.Requisicoes)
        {
            var tudo = ProvedorIaRoteirizado.Serializar(requisicao) + JsonSerializer.Serialize(requisicao.Ferramentas.Select(f => f.EsquemaParametros.GetRawText()));
            tudo.Should().NotContain(AmbienteAgente.TelefoneCliente).And.NotContain("71988887777").And.NotContain("988887777").And.NotContain("98888-7777");
            tudo.Should().NotContain("Fulano").And.NotContain("Sigiloso").And.NotContain("Beltrano").And.NotContain("beltrano@")
                .And.NotContain("Cliente Sobrenome");
            tudo.Should().NotContain(AmbienteAgente.ChaveGemini);
            tudo.Should().NotContain(c.NegocioId.ToString()).And.NotContain(c.NomeInstancia);
        }

        // O horário ocupado simplesmente não aparece (e nada diz quem o ocupa).
        var horas = HorasDoUltimoResultado(_ambiente.Provedor.Requisicoes[2]);
        horas.Should().NotContain("10:00");
    }

    // ---------------------------------------------------------------- entrada e histórico (A1b, A4)

    [Fact]
    public async Task Mensagem_gigante_chega_truncada_e_historico_de_100_mensagens_vai_com_no_maximo_20_sem_ferramentas()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.EnviarAsync(c, "primeira"); // cria a conversa
        var conversa = (await _ambiente.ConversasAsync()).Single();

        await _ambiente.NoBancoAsync(async db =>
        {
            for (var i = 0; i < 100; i++)
            {
                db.MensagensWhatsApp.Add(new MensagemWhatsApp(
                    c.NegocioId, conversa.InstanciaId, conversa.Id, null,
                    i % 2 == 0 ? PapelMensagemWhatsApp.Usuario : PapelMensagemWhatsApp.Assistente, $"histórico {i}"));
            }

            db.MensagensWhatsApp.Add(new MensagemWhatsApp(c.NegocioId, conversa.InstanciaId, conversa.Id, null, PapelMensagemWhatsApp.Ferramenta, "{}", "listar_servicos"));
            return await db.SaveChangesAsync();
        });
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("ok");

        await _ambiente.EnviarAsync(c, new string('x', 50_000) + "\u0007\u0000fim");

        var requisicao = _ambiente.Provedor.Requisicoes[^1];
        requisicao.Mensagens.Should().OnlyContain(m => m.Papel != PapelIa.Ferramenta);
        requisicao.Mensagens.Count.Should().BeLessThanOrEqualTo(20);
        requisicao.Mensagens[0].Papel.Should().Be(PapelIa.Usuario, "o histórico sempre começa por uma fala do cliente");
        requisicao.Mensagens.Sum(m => m.Texto!.Length).Should().BeLessThanOrEqualTo(8000 + 20 * 2);
        requisicao.Mensagens.Should().OnlyContain(m => m.Texto!.Length <= 1000 * 20);
        var ultima = requisicao.Mensagens[^1];
        ultima.Papel.Should().Be(PapelIa.Usuario);
        ultima.Texto!.Split('\n').Last().Length.Should().BeLessThanOrEqualTo(1000);
        ultima.Texto.Should().NotContain("\u0007").And.NotContain("\u0000");
        requisicao.MaxTokensSaida.Should().Be(512);
    }

    // ---------------------------------------------------------------- limites (A4)

    [Fact]
    public async Task Limite_por_telefone_na_hora_avisa_uma_vez_e_depois_silencio_sem_chamar_a_IA()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("resposta normal");

        for (var i = 0; i < 3; i++)
            await _ambiente.EnviarAsync(c, $"mensagem {i}");

        _ambiente.Provedor.Chamadas.Should().Be(3);
        _ambiente.Evolution.Envios.Should().HaveCount(3);

        await _ambiente.EnviarAsync(c, "mensagem 4");
        _ambiente.Provedor.Chamadas.Should().Be(3, "a 4ª passou do limite de 3/hora e não chama o modelo");
        _ambiente.Evolution.Envios.Should().HaveCount(4);
        _ambiente.UltimaEntrega().Should().Contain("pausar").And.Contain(c.Slug);

        for (var i = 5; i <= 12; i++)
            await _ambiente.EnviarAsync(c, $"mensagem {i}");

        _ambiente.Provedor.Chamadas.Should().Be(3);
        _ambiente.Evolution.Envios.Should().HaveCount(4, "depois do aviso, silêncio");
        (await _ambiente.UsosAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task Cota_mensal_de_tokens_estourada_nao_chama_a_IA()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.EnviarAsync(c, "oi"); // cria a conversa (e gasta 1 chamada)
        var conversa = (await _ambiente.ConversasAsync()).Single();
        await _ambiente.NoBancoAsync(async db =>
        {
            db.UsosIa.Add(new UsoIa(c.NegocioId, conversa.Id, "modelo-falso", 90_000, 10_000, true));
            return await db.SaveChangesAsync();
        });
        var antes = _ambiente.Provedor.Chamadas;
        var enviosAntes = _ambiente.Evolution.Envios.Count;

        await _ambiente.EnviarAsync(c, "e agora?");

        _ambiente.Provedor.Chamadas.Should().Be(antes);
        _ambiente.Evolution.Envios.Should().HaveCount(enviosAntes + 1);
        _ambiente.UltimaEntrega().Should().Contain("acesse");

        await _ambiente.EnviarAsync(c, "de novo?");
        _ambiente.Evolution.Envios.Should().HaveCount(enviosAntes + 1);
    }

    [Fact]
    public async Task Teto_diario_de_chamadas_do_negocio_vale_para_todos_os_telefones_do_negocio()
    {
        await _ambiente.DisposeAsync();
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(new Dictionary<string, string?> { ["Ai:LimiteChamadasPorNegocioPorDia"] = "2" });
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("ok");

        await _ambiente.EnviarAsync(c, "um", telefone: "5571911110001");
        await _ambiente.EnviarAsync(c, "dois", telefone: "5571911110002");
        await _ambiente.EnviarAsync(c, "três", telefone: "5571911110003");

        _ambiente.Provedor.Chamadas.Should().Be(2);
        _ambiente.UltimaEntrega().Should().Contain("pausar");
    }

    [Fact]
    public async Task Rajada_de_10_mensagens_em_paralelo_na_mesma_conversa_vira_no_maximo_2_rodadas()
    {
        await _ambiente.DisposeAsync();
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(new Dictionary<string, string?> { ["Ai:LimiteMensagensPorTelefonePorHora"] = "100" });
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Atraso = TimeSpan.FromMilliseconds(600);
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("ok");

        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => _ambiente.EnviarAsync(c, $"rajada {i}")));

        _ambiente.Provedor.Chamadas.Should().BeInRange(1, 2);
        (await _ambiente.MensagensAsync()).Count(m => m.Papel == PapelMensagemWhatsApp.Usuario).Should().Be(10);
        (await _ambiente.ConversasAsync()).Single().ProcessandoAte.Should().BeNull("o lease é solto no fim");
    }

    // ---------------------------------------------------------------- assinatura, Desligado, piloto

    [Fact]
    public async Task Assinatura_suspensa_deixa_o_agente_em_silencio_e_nao_chama_a_IA()
    {
        var c = await _ambiente.SemearAsync();
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

        (await _ambiente.EnviarAsync(c, "oi")).StatusCode.Should().Be(HttpStatusCode.OK);

        _ambiente.Provedor.Chamadas.Should().Be(0);
        _ambiente.Evolution.Envios.Should().BeEmpty();
        (await _ambiente.MensagensAsync()).Should().ContainSingle(m => m.Papel == PapelMensagemWhatsApp.Usuario, "a mensagem fica salva");
    }

    [Fact]
    public async Task Provider_Desligado_salva_a_mensagem_e_nao_responde()
    {
        await _ambiente.DisposeAsync();
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(provedorIa: "Desligado");
        var c = await _ambiente.SemearAsync();

        await _ambiente.EnviarAsync(c, "oi");

        (await _ambiente.MensagensAsync()).Should().ContainSingle();
        _ambiente.Evolution.Envios.Should().BeEmpty();
        _ambiente.Provedor.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Provider_Eco_continua_ecoando_sem_chave_e_sem_chamar_a_IA()
    {
        await _ambiente.DisposeAsync();
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(provedorIa: "Eco");
        var c = await _ambiente.SemearAsync();

        await _ambiente.EnviarAsync(c, "Quero cortar");

        _ambiente.UltimaEntrega().Should().Be("Recebi: Quero cortar");
        _ambiente.Provedor.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Negocios_habilitados_restringe_o_piloto_e_nao_afeta_os_demais_so_o_agente()
    {
        var piloto = await _ambiente.SemearAsync("Piloto");
        var outro = await _ambiente.SemearAsync("Outro");
        var opcoes = _ambiente.Fabrica.Services.GetRequiredService<IOptions<OpcoesIa>>().Value;
        opcoes.NegociosHabilitados = piloto.NegocioId.ToString();
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("ok");

        await _ambiente.EnviarAsync(outro, "oi");
        _ambiente.Provedor.Chamadas.Should().Be(0);
        _ambiente.Evolution.Envios.Should().BeEmpty();

        await _ambiente.EnviarAsync(piloto, "oi");
        _ambiente.Provedor.Chamadas.Should().Be(1);
        _ambiente.Evolution.Envios.Should().ContainSingle();
    }

    // ---------------------------------------------------------------- fila real e recuperação

    [Fact]
    public async Task Webhook_responde_200_sem_esperar_a_IA_e_a_fila_real_entrega_a_resposta_depois()
    {
        await _ambiente.DisposeAsync();
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(fila: typeof(void));
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Atraso = TimeSpan.FromSeconds(6);
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("resposta da fila");

        var inicio = DateTime.UtcNow;
        (await _ambiente.EnviarAsync(c, "oi")).StatusCode.Should().Be(HttpStatusCode.OK);
        (DateTime.UtcNow - inicio).Should().BeLessThan(TimeSpan.FromSeconds(4), "o webhook não espera o modelo (6 s)");
        _ambiente.Evolution.Envios.Should().BeEmpty("a IA ainda está pensando");

        await EsperarAsync(() => _ambiente.Evolution.Envios.Count == 1, TimeSpan.FromSeconds(30));
        _ambiente.UltimaEntrega().Should().Be("resposta da fila");
    }

    [Fact]
    public async Task Recuperacao_responde_mensagem_perdida_e_descarta_sem_responder_a_com_mais_de_10_minutos()
    {
        await _ambiente.DisposeAsync();
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(fila: typeof(FilaQuePerde));
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("de volta");

        // A fila "perdeu" a mensagem (reinício): nada é respondido.
        await _ambiente.EnviarAsync(c, "oi, estão abertos?");
        _ambiente.Evolution.Envios.Should().BeEmpty();

        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
        {
            var recuperador = escopo.ServiceProvider.GetRequiredService<RecuperadorRespostasAgente>();
            (await recuperador.ListarPendentesAsync(DateTimeOffset.UtcNow, CancellationToken.None)).Should().BeEmpty("recente demais (< 30 s)");
            var pendentes = await recuperador.ListarPendentesAsync(DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);
            var conversaId = pendentes.Should().ContainSingle().Subject;

            await escopo.ServiceProvider.GetRequiredService<IProcessadorRodadaAgente>().ProcessarAsync(conversaId);
        }

        _ambiente.UltimaEntrega().Should().Be("de volta");

        // Segunda mensagem perdida, agora velha (15 min): marcada como tratada, sem resposta.
        await _ambiente.EnviarAsync(c, "ainda aí?");
        await _ambiente.NoBancoAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE mensagens_whatsapp SET criado_em = now() - interval '15 minutes' WHERE conteudo = 'ainda aí?'; " +
            "UPDATE mensagens_whatsapp SET criado_em = now() - interval '18 minutes' WHERE conteudo <> 'ainda aí?'; " +
            "UPDATE conversas_whatsapp SET processada_ate = now() - interval '17 minutes'"));
        var enviosAntes = _ambiente.Evolution.Envios.Count;
        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
        {
            var pendentes = await escopo.ServiceProvider.GetRequiredService<RecuperadorRespostasAgente>()
                .ListarPendentesAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            await escopo.ServiceProvider.GetRequiredService<IProcessadorRodadaAgente>().ProcessarAsync(pendentes.Single());
            (await escopo.ServiceProvider.GetRequiredService<RecuperadorRespostasAgente>()
                .ListarPendentesAsync(DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None)).Should().BeEmpty("foi dada como tratada");
        }

        _ambiente.Evolution.Envios.Should().HaveCount(enviosAntes);
        _ambiente.Provedor.Chamadas.Should().Be(1);
    }

    [Fact]
    public async Task Endpoint_publico_de_horarios_continua_igual_apos_a_extracao_do_servico()
    {
        var c = await _ambiente.SemearAsync();
        var dia = AmbienteAgente.ProximaSegunda().ToString("yyyy-MM-dd");
        using var cliente = _ambiente.ClientePublico(c.Slug);

        var comAna = await cliente.GetFromJsonAsync<List<HorarioLivrePublico>>(
            $"/publico/horarios-livres?data={dia}&duracaoMinutos=30&servicoIds={c.Corte}&profissionalId={c.Ana}");
        var qualquer = await cliente.GetFromJsonAsync<List<HorarioLivrePublico>>(
            $"/publico/horarios-livres?data={dia}&duracaoMinutos=30&servicoIds={c.Corte}");

        comAna!.Should().NotBeEmpty().And.OnlyContain(h => h.ProfissionalId == c.Ana);
        comAna.Count.Should().Be(26, "Ana faz o corte em 60 min: 9h–11h e 13h–17h na grade de 15 min");
        qualquer!.Should().HaveCount(37).And.BeInAscendingOrder(h => h.Inicio);
        qualquer.Select(h => h.ProfissionalId).Distinct().Should().BeEquivalentTo([c.Ana, c.Beto]);
    }

    // ---------------------------------------------------------------- apoio

    private async Task<List<string>> HorasPublicasAsync(CenarioAgente c, string consulta)
    {
        using var cliente = _ambiente.ClientePublico(c.Slug);
        var horarios = await cliente.GetFromJsonAsync<List<HorarioLivrePublico>>($"/publico/horarios-livres?{consulta}");
        return horarios!.Select(h => AmbienteAgente.HoraLocal(h.Inicio)).ToList();
    }

    private static List<string> HorasDoUltimoResultado(AiRequest requisicao) =>
        ProvedorIaRoteirizado.Resultados(requisicao).Last(r => r.Nome == "consultar_horarios").Conteudo
            .GetProperty("horarios").EnumerateArray().Select(h => h.GetProperty("hora").GetString()!).ToList();

    private static async Task EsperarAsync(Func<bool> condicao, TimeSpan limite)
    {
        var fim = DateTime.UtcNow + limite;
        while (!condicao() && DateTime.UtcNow < fim)
            await Task.Delay(100);
        condicao().Should().BeTrue("a condição deveria ter sido atendida dentro de {0}", limite);
    }
}
