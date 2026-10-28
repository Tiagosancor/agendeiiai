using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Aplicacao.WhatsAppAgente.Ia;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.WhatsAppAgente.HelpersAgendamentoAgente;
using static Plataforma.Testes.Integracao.WhatsAppAgente.ProvedorIaRoteirizado;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Fase 4, fatia 1: cada rodada do agente deixa UMA linha em <c>rodadas_agente</c> (desfecho, ferramentas, tokens, latência) e a
/// chamada ao modelo leva os campos extras em <c>usos_ia</c> — tudo sem dado pessoal. Provedor roteirizado, nunca o Gemini real.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class RodadasAgenteTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public RodadasAgenteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

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
            ["Ai:MaxChamadasFerramentaPorIteracao"] = "10",
        };
        foreach (var (chave, valor) in extra ?? new Dictionary<string, string?>())
            config[chave] = valor;
        await _ambiente.IniciarAsync(config);
    }

    private Task<List<RodadaAgente>> RodadasAsync() =>
        _ambiente.NoBancoAsync(db => db.RodadasAgente.IgnoreQueryFilters().AsNoTracking().OrderBy(r => r.IniciadaEm).ToListAsync());

    // ---------------------------------------------------------------- desfechos

    [Fact]
    public async Task Resposta_normal_grava_uma_rodada_respondida_ligada_ao_uso_da_chamada()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Olá! Como posso ajudar?");

        await _ambiente.EnviarAsync(c, "oi");

        var rodada = (await RodadasAsync()).Should().ContainSingle().Subject;
        rodada.NegocioId.Should().Be(c.NegocioId);
        rodada.Desfecho.Should().Be(DesfechoRodadaAgente.Respondida);
        rodada.MotivoSilencio.Should().BeNull();
        rodada.Chamadas.Should().Be(1);
        rodada.TokensEntrada.Should().Be(100);
        rodada.TokensSaida.Should().Be(20);
        rodada.Modelo.Should().Be("modelo-falso");
        rodada.MotivoFim.Should().Be("STOP");
        rodada.PromptVersao.Should().Be("v2");
        rodada.Ferramentas.Should().BeNull();
        rodada.DuracaoMs.Should().BeGreaterThanOrEqualTo(0);

        var uso = (await _ambiente.UsosAsync()).Should().ContainSingle().Subject;
        uso.RodadaId.Should().Be(rodada.Id);
        uso.MotivoFim.Should().Be("STOP");
        uso.Iteracao.Should().Be(1);
        uso.LatenciaMs.Should().BeGreaterThanOrEqualTo(0);
        uso.FalhaTipo.Should().BeNull();
        uso.StatusHttp.Should().BeNull();
    }

    [Fact]
    public async Task Ferramentas_chamadas_e_erros_viram_nomes_e_codigos_e_cada_volta_e_um_uso()
    {
        var c = await _ambiente.SemearAsync();
        var dia = AmbienteAgente.ProximaSegunda().ToString("yyyy-MM-dd");
        _ambiente.Provedor.Roteiro = (_, n) => n switch
        {
            1 => Chama(("listar_servicos", "{}")),
            2 => Chama(("consultar_horarios", $$"""{"servico_id":"{{Guid.NewGuid()}}","data":"{{dia}}"}""")),
            _ => Texto("Não achei esse serviço."),
        };

        await _ambiente.EnviarAsync(c, "tem horário?");

        var rodada = (await RodadasAsync()).Should().ContainSingle().Subject;
        rodada.Desfecho.Should().Be(DesfechoRodadaAgente.Respondida);
        rodada.Chamadas.Should().Be(3);
        rodada.TokensEntrada.Should().Be(300);
        rodada.Ferramentas.Should().Be("listar_servicos,consultar_horarios");
        rodada.ErrosFerramenta.Should().Be("servico_desconhecido");

        var usos = await _ambiente.UsosAsync();
        usos.Select(u => u.Iteracao).Should().Equal(1, 2, 3);
        usos.Should().OnlyContain(u => u.RodadaId == rodada.Id);
    }

    [Fact]
    public async Task Falha_do_provedor_vira_fallback_com_o_tipo_e_o_status_http()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.FalharComHttp500 = true;

        await _ambiente.EnviarAsync(c, "oi");

        var rodada = (await RodadasAsync()).Should().ContainSingle().Subject;
        rodada.Desfecho.Should().Be(DesfechoRodadaAgente.Fallback);
        rodada.FalhaProvedor.Should().Be("http:500");
        rodada.Chamadas.Should().Be(1);
        rodada.TokensEntrada.Should().Be(0);

        var uso = (await _ambiente.UsosAsync()).Should().ContainSingle().Subject;
        uso.Sucesso.Should().BeFalse();
        uso.FalhaTipo.Should().Be("http");
        uso.StatusHttp.Should().Be(500);
        uso.RodadaId.Should().Be(rodada.Id);
    }

    [Fact]
    public async Task Resposta_cortada_por_tokens_duas_vezes_vira_fallback_com_MAX_TOKENS()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => new AiResponse("meio da fra", [], 100, 1536, "modelo-falso", "MAX_TOKENS");

        await _ambiente.EnviarAsync(c, "oi");

        var rodada = (await RodadasAsync()).Should().ContainSingle().Subject;
        rodada.Desfecho.Should().Be(DesfechoRodadaAgente.Fallback);
        rodada.MotivoFim.Should().Be("MAX_TOKENS");
        rodada.Chamadas.Should().Be(2);
    }

    [Fact]
    public async Task Laco_de_ferramentas_sem_fim_vira_fallback_marcado_como_LACO_FERRAMENTAS()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Chama(("listar_servicos", "{}"));

        await _ambiente.EnviarAsync(c, "oi");

        var rodada = (await RodadasAsync()).Should().ContainSingle().Subject;
        rodada.Desfecho.Should().Be(DesfechoRodadaAgente.Fallback);
        rodada.MotivoFim.Should().Be("LACO_FERRAMENTAS");
        rodada.Ferramentas.Should().Contain("listar_servicos");
    }

    [Fact]
    public async Task Agente_desligado_na_instancia_e_silencio_com_motivo_e_sem_chamar_a_IA()
    {
        var c = await _ambiente.SemearAsync(agenteAtivo: false);

        await _ambiente.EnviarAsync(c, "oi");
        (await RodadasAsync()).Should().BeEmpty("o webhook nem enfileira com o agente desligado");
        await _ambiente.ProcessarConversaAsync();

        var rodada = (await RodadasAsync()).Should().ContainSingle().Subject;
        rodada.Desfecho.Should().Be(DesfechoRodadaAgente.Silencio);
        rodada.MotivoSilencio.Should().Be(MotivoSilencioRodada.AgenteDesligado);
        rodada.Chamadas.Should().Be(0);
        _ambiente.Provedor.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Negocio_fora_do_piloto_e_silencio_ForaDoPiloto()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:NegociosHabilitados"] = Guid.NewGuid().ToString() });
        var c = await _ambiente.SemearAsync();

        await _ambiente.EnviarAsync(c, "oi");

        var rodada = (await RodadasAsync()).Should().ContainSingle().Subject;
        rodada.Desfecho.Should().Be(DesfechoRodadaAgente.Silencio);
        rodada.MotivoSilencio.Should().Be(MotivoSilencioRodada.ForaDoPiloto);
    }

    [Fact]
    public async Task Conversa_com_atendimento_humano_e_silencio_Humano()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.EnviarAsync(c, "oi");
        await _ambiente.NoBancoAsync(async db =>
            await db.Database.ExecuteSqlRawAsync("UPDATE conversas_whatsapp SET status = 'Humano'"));
        (await RodadasAsync()).Should().ContainSingle();

        await _ambiente.EnviarAsync(c, "alguém aí?");

        var rodadas = await RodadasAsync();
        rodadas.Should().HaveCount(1, "em Humano a conversa nem é enfileirada pelo webhook");
        await _ambiente.ProcessarConversaAsync();
        (await RodadasAsync()).Last().Should().Match<RodadaAgente>(r =>
            r.Desfecho == DesfechoRodadaAgente.Silencio && r.MotivoSilencio == MotivoSilencioRodada.Humano);
    }

    [Fact]
    public async Task Limite_de_mensagens_por_telefone_grava_LimiteTelefone()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:LimiteMensagensPorTelefonePorHora"] = "1" });
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("ok");

        await _ambiente.EnviarAsync(c, "oi");
        await _ambiente.EnviarAsync(c, "e aí");

        var rodadas = await RodadasAsync();
        rodadas.Select(r => r.Desfecho).Should().Equal(DesfechoRodadaAgente.Respondida, DesfechoRodadaAgente.LimiteTelefone);
        rodadas[1].Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Limite_de_chamadas_do_negocio_grava_LimiteCusto()
    {
        await IniciarAsync(new Dictionary<string, string?> { ["Ai:LimiteChamadasPorNegocioPorDia"] = "1" });
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("ok");

        await _ambiente.EnviarAsync(c, "oi");
        await _ambiente.EnviarAsync(c, "e aí");

        (await RodadasAsync()).Select(r => r.Desfecho).Should().Equal(DesfechoRodadaAgente.Respondida, DesfechoRodadaAgente.LimiteCusto);
    }

    [Fact]
    public async Task Preparar_agendamento_grava_ResumoEnviado_e_o_sim_grava_Confirmacao_sem_chamar_a_IA()
    {
        var c = await _ambiente.SemearAsync();
        (await _ambiente.PrepararAsync(c, Prepara(c, c.Ana, "10:00", "Maria Souza"))).Should().BeNull();

        var primeira = (await RodadasAsync()).Should().ContainSingle().Subject;
        primeira.Desfecho.Should().Be(DesfechoRodadaAgente.ResumoEnviado);
        primeira.Ferramentas.Should().Be("preparar_agendamento");

        await _ambiente.EnviarAsync(c, "Sim!");

        var rodadas = await RodadasAsync();
        rodadas.Should().HaveCount(2);
        rodadas[1].Desfecho.Should().Be(DesfechoRodadaAgente.Confirmacao);
        rodadas[1].Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Provedor_Eco_tambem_registra_a_rodada_sem_tokens()
    {
        await _ambiente.DisposeAsync();
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(provedorIa: "Eco");
        var c = await _ambiente.SemearAsync();

        await _ambiente.EnviarAsync(c, "oi");

        var rodada = (await RodadasAsync()).Should().ContainSingle().Subject;
        rodada.Desfecho.Should().Be(DesfechoRodadaAgente.Respondida);
        rodada.Chamadas.Should().Be(0);
        rodada.PromptVersao.Should().Be("eco");
    }

    // ---------------------------------------------------------------- privacidade

    [Fact]
    public async Task Nenhuma_coluna_de_texto_guarda_telefone_email_nome_ou_texto_da_conversa()
    {
        var c = await _ambiente.SemearAsync(nome: "Barbearia Segredo Absoluto");
        const string vazamento = "joao.silva@exemplo.com";
        _ambiente.Provedor.Roteiro = (_, n) => n switch
        {
            // Modelo "comprometido": inventa ferramenta com dado pessoal no nome e nos argumentos.
            1 => Chama(("apagar_" + vazamento, $$"""{"telefone":"{{AmbienteAgente.TelefoneCliente}}"}"""),
                ("consultar_horarios", $$"""{"servico_id":"{{vazamento}}","data":"2026-01-01"}""")),
            _ => Texto("Seu e-mail " + vazamento + " e seu telefone " + AmbienteAgente.TelefoneCliente),
        };

        await _ambiente.EnviarAsync(c, $"meu email é {vazamento}, meu telefone é 71 98888-7777 e meu nome é Fulano de Tal");

        var rodada = (await RodadasAsync()).Should().ContainSingle().Subject;
        rodada.Ferramentas.Should().Be("ferramenta_desconhecida,consultar_horarios");

        var textos = await _ambiente.NoBancoAsync(async db =>
        {
            var linhas = new List<string>();
            foreach (var tabela in new[] { "rodadas_agente", "usos_ia" })
            {
                var conexao = db.Database.GetDbConnection();
                await conexao.OpenAsync();
                try
                {
                    await using var comando = conexao.CreateCommand();
                    comando.CommandText = $"SELECT row_to_json(t)::text FROM {tabela} t";
                    await using var leitor = await comando.ExecuteReaderAsync();
                    while (await leitor.ReadAsync())
                        linhas.Add(leitor.GetString(0));
                }
                finally
                {
                    await conexao.CloseAsync();
                }
            }

            return linhas;
        });

        textos.Should().NotBeEmpty();
        var tudo = string.Join('\n', textos);
        foreach (var proibido in new[] { vazamento, "exemplo.com", "98888", "5571988887777", "Fulano", "Segredo", "Cliente", "oi ", AmbienteAgente.ChaveGemini })
            tudo.Should().NotContain(proibido);
    }

    [Fact]
    public async Task Motivo_de_fim_e_tipo_de_falha_fora_da_lista_fechada_viram_DESCONHECIDO()
    {
        var c = await _ambiente.SemearAsync();
        const string pessoal = "joao.silva@exemplo.com";
        _ambiente.Provedor.Roteiro = (_, _) => new AiResponse("ok", [], 100, 20, "modelo-falso", pessoal + " 71988887777");
        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.Provedor.Roteiro = (_, _) => throw new FalhaProvedorIaException(pessoal, 500);
        await _ambiente.EnviarAsync(c, "oi de novo");

        var rodadas = await RodadasAsync();
        rodadas.Should().HaveCount(2);
        rodadas[0].MotivoFim.Should().Be("DESCONHECIDO");
        rodadas[1].FalhaProvedor.Should().Be("DESCONHECIDO:500");
        var usos = await _ambiente.UsosAsync();
        usos[0].MotivoFim.Should().Be("DESCONHECIDO");
        usos[1].FalhaTipo.Should().Be("DESCONHECIDO");
    }

    /// <summary>Cancela (como um desligamento do processo) quando a mensagem do resumo vai ser gravada, antes de qualquer envio.</summary>
    private sealed class CancelaNaGravacaoDoResumo : DbCommandInterceptor
    {
        public CancellationTokenSource? Fonte { get; set; }

        public bool Disparou { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Verificar(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Verificar(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Verificar(DbCommand comando)
        {
            if (Fonte is null || !comando.CommandText.Contains("INSERT INTO mensagens_whatsapp", StringComparison.Ordinal))
                return;

            if (comando.Parameters.Cast<DbParameter>().Any(p => p.Value is string v && v == MarcadoresAgente.Resumo))
            {
                Disparou = true;
                Fonte.Cancel();
                throw new OperationCanceledException(Fonte.Token);
            }
        }
    }

    [Fact]
    public async Task Diagnostico_nunca_persiste_entidades_funcionais_pendentes_da_rodada_cancelada()
    {
        var gancho = new CancelaNaGravacaoDoResumo();
        await _ambiente.DisposeAsync();
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(
            new Dictionary<string, string?>
            {
                ["Ai:Agendamento:Modo"] = "Todos",
                ["Ai:LimiteMensagensPorTelefonePorHora"] = "1000",
                ["Ai:LimiteMensagensPorTelefonePorDia"] = "5000",
            },
            fila: typeof(FilaQuePerde),
            ajustarServicos: servicos => servicos.ConfigureDbContext<PlataformaDbContext>(o => o.AddInterceptors(gancho)));
        var c = await _ambiente.SemearAsync();
        var argumentos = Prepara(c, c.Ana, "10:00", "Maria Souza");
        _ambiente.Provedor.Roteiro = (_, n) => n == 1 ? Chama(("preparar_agendamento", argumentos)) : Texto("ok");

        await _ambiente.EnviarAsync(c, "quero agendar");
        var conversa = (await _ambiente.ConversasAsync()).Single();
        using var fonte = new CancellationTokenSource();
        gancho.Fonte = fonte;
        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
        {
            var processador = escopo.ServiceProvider.GetRequiredService<IProcessadorRodadaAgente>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processador.ProcessarAsync(conversa.Id, fonte.Token));
        }

        gancho.Disparou.Should().BeTrue("o teste precisa ter cancelado exatamente na gravação do resumo");
        gancho.Fonte = null;

        // Nada funcional foi gravado pelo diagnóstico: nem a mensagem do resumo, nem o "resumo enviado" da pendência, nem envio ao cliente.
        (await _ambiente.MarcadoresAsync(MarcadoresAgente.Resumo)).Should().BeEmpty();
        var pendencia = (await _ambiente.PendenciasAsync()).Should().ContainSingle().Subject;
        pendencia.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
        pendencia.ResumoMensagemId.Should().BeNull();
        pendencia.ResumoEnviadoEm.Should().BeNull();
        _ambiente.Evolution.Envios.Should().BeEmpty();

        // E o diagnóstico em si foi gravado (contexto próprio), com o negócio certo.
        var rodada = (await RodadasAsync()).Should().ContainSingle().Subject;
        rodada.NegocioId.Should().Be(c.NegocioId);
    }

    // ---------------------------------------------------------------- flag, falha ao gravar, compatibilidade

    [Fact]
    public async Task Com_o_registro_desligado_nada_e_gravado_e_o_agente_responde_igual()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Resposta de sempre");
        await _ambiente.EnviarAsync(c, "oi");
        _ambiente.UltimaEntrega().Should().EndWith("Resposta de sempre");

        await IniciarAsync(new Dictionary<string, string?> { ["Ai:RegistrarRodadas"] = "false" });
        c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Resposta de sempre");
        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.UltimaEntrega().Should().EndWith("Resposta de sempre");
        _ambiente.Evolution.Envios.Should().ContainSingle();
        (await RodadasAsync()).Should().BeEmpty();
        var uso = (await _ambiente.UsosAsync()).Should().ContainSingle().Subject;
        uso.RodadaId.Should().BeNull();
        uso.MotivoFim.Should().BeNull();
        uso.LatenciaMs.Should().BeNull();
        uso.Iteracao.Should().BeNull();
        uso.Sucesso.Should().BeTrue();
    }

    [Fact]
    public async Task Falha_ao_gravar_a_rodada_nunca_derruba_nem_altera_a_resposta_ao_cliente()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => Texto("Resposta ao cliente");
        await _ambiente.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION falha_rodada() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'falha simulada'; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_falha_rodada BEFORE INSERT ON rodadas_agente FOR EACH ROW EXECUTE FUNCTION falha_rodada();
                """);
            return 0;
        });

        try
        {
            var resposta = await _ambiente.EnviarAsync(c, "oi");

            resposta.IsSuccessStatusCode.Should().BeTrue();
            _ambiente.UltimaEntrega().Should().EndWith("Resposta ao cliente", "o aviso de atendimento automatizado vai colado antes");
            (await RodadasAsync()).Should().BeEmpty();
            (await _ambiente.MensagensAsync()).Should().Contain(m => m.Papel == PapelMensagemWhatsApp.Assistente && m.Conteudo.EndsWith("Resposta ao cliente"));
            var conversa = (await _ambiente.ConversasAsync()).Single();
            conversa.ProcessadaAte.Should().NotBeNull();
            conversa.ProcessandoAte.Should().BeNull("o lease é solto mesmo com a falha");

            // E a conversa segue funcionando depois da falha de registro.
            await _ambiente.EnviarAsync(c, "tudo bem?");
            _ambiente.Evolution.Envios.Should().HaveCount(2);
        }
        finally
        {
            // O banco do teste é compartilhado: sem isto o gatilho derrubaria os testes seguintes.
            await _ambiente.NoBancoAsync(async db =>
            {
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS trg_falha_rodada ON rodadas_agente; DROP FUNCTION IF EXISTS falha_rodada();");
                return 0;
            });
        }
    }

    [Fact]
    public async Task Linha_de_uso_no_formato_da_versao_anterior_continua_valida_com_o_banco_migrado()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.EnviarAsync(c, "oi");
        var conversa = (await _ambiente.ConversasAsync()).Single();

        // INSERT exatamente como a versão anterior do código fazia: sem as colunas novas.
        await _ambiente.NoBancoAsync(async db =>
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO usos_ia (id, negocio_id, conversa_id, modelo, tokens_entrada, tokens_saida, sucesso, criado_em)
                VALUES ({Guid.NewGuid()}, {c.NegocioId}, {conversa.Id}, 'modelo-antigo', 1, 2, true, now())
                """));

        var usos = await _ambiente.UsosAsync();
        usos.Should().Contain(u => u.Modelo == "modelo-antigo" && u.RodadaId == null && u.LatenciaMs == null);
    }

    // ---------------------------------------------------------------- multi-tenant e retenção

    [Fact]
    public async Task Rodadas_de_um_negocio_nao_aparecem_para_o_outro()
    {
        var a = await _ambiente.SemearAsync("Negócio A");
        var b = await _ambiente.SemearAsync("Negócio B");
        _ambiente.Provedor.Roteiro = (_, _) => Texto("ok");
        await _ambiente.EnviarAsync(a, "oi");
        await _ambiente.EnviarAsync(b, "oi");

        (await RodadasAsync()).Should().HaveCount(2);
        foreach (var (visivel, oculto) in new[] { (a.NegocioId, b.NegocioId), (b.NegocioId, a.NegocioId) })
        {
            using var escopo = _ambiente.Fabrica.Services.CreateScope();
            escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(visivel);
            var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            var rodadas = await db.RodadasAgente.AsNoTracking().ToListAsync();
            rodadas.Should().ContainSingle().Which.NegocioId.Should().Be(visivel);
            rodadas.Should().NotContain(r => r.NegocioId == oculto);
        }
    }

    [Fact]
    public async Task Retencao_apaga_so_as_rodadas_antigas_e_deixa_usos_e_rodadas_recentes()
    {
        var c = await _ambiente.SemearAsync();
        var outro = await _ambiente.SemearAsync("Outro negócio");
        _ambiente.Provedor.Roteiro = (_, _) => Texto("ok");
        await _ambiente.EnviarAsync(c, "oi");
        await _ambiente.EnviarAsync(outro, "oi");
        var conversas = await _ambiente.ConversasAsync();
        var conversaC = conversas.Single(x => x.NegocioId == c.NegocioId);
        var conversaOutro = conversas.Single(x => x.NegocioId == outro.NegocioId);
        var usosAntes = (await _ambiente.UsosAsync()).Count;

        var agora = DateTimeOffset.UtcNow;
        var velhaC = Guid.NewGuid();
        var velhaOutro = Guid.NewGuid();
        var quaseNoLimite = Guid.NewGuid();
        await _ambiente.NoBancoAsync(async db =>
        {
            db.RodadasAgente.AddRange(
                Nova(velhaC, c.NegocioId, conversaC.Id, agora.AddDays(-91)),
                Nova(velhaOutro, outro.NegocioId, conversaOutro.Id, agora.AddDays(-400)),
                Nova(quaseNoLimite, c.NegocioId, conversaC.Id, agora.AddDays(-89)));
            await db.SaveChangesAsync();
            return 0;
        });
        (await RodadasAsync()).Should().HaveCount(5);

        using (var escopo = _ambiente.Fabrica.Services.CreateScope())
        {
            var removidas = await escopo.ServiceProvider.GetRequiredService<JobLimparRodadasAgente>().ExecutarAsync();
            removidas.Should().Be(2);
        }

        var restantes = await RodadasAsync();
        restantes.Select(r => r.Id).Should().NotContain([velhaC, velhaOutro]).And.Contain(quaseNoLimite);
        restantes.Should().HaveCount(3, "as duas rodadas reais de agora e a de 89 dias");
        (await _ambiente.UsosAsync()).Should().HaveCount(usosAntes, "usos_ia é base de cota e não entra na retenção");
        (await _ambiente.MensagensAsync()).Should().NotBeEmpty();
    }

    private static RodadaAgente Nova(Guid id, Guid negocioId, Guid conversaId, DateTimeOffset iniciadaEm) =>
        new(id, negocioId, conversaId, iniciadaEm, 10, DesfechoRodadaAgente.Respondida, null, 1, 1, 1, "m", "STOP", null, null, null, "v1");
}
