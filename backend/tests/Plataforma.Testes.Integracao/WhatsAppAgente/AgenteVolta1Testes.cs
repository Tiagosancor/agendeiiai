using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>Volta 1 da revisão: limites dentro da rodada, lease com dono, lote estável do histórico, texto fixo e minimização.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AgenteVolta1Testes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public AgenteVolta1Testes(PostgresContainerFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_ambiente is not null)
            await _ambiente.DisposeAsync();
    }

    private readonly InterceptadorDeConsultas _interceptador = new();

    private async Task IniciarAsync(Dictionary<string, string?>? config = null)
    {
        _ambiente = new AmbienteAgente(_postgres);
        var todas = new Dictionary<string, string?> { ["Ai:LimiteMensagensPorTelefonePorHora"] = "1000", ["Ai:LimiteMensagensPorTelefonePorDia"] = "5000" };
        foreach (var (k, v) in config ?? [])
            todas[k] = v;
        await _ambiente.IniciarAsync(todas, ajustarServicos: s => s.ConfigureDbContext<PlataformaDbContext>(o => o.AddInterceptors(_interceptador)));
    }

    // O histórico seleciona (papel, conteudo) ordenado do mais novo ao mais velho com LIMIT; a consulta das pendentes seleciona
    // (criado_em, conteudo).
    private static bool EhConsultaDoHistorico(string sql) =>
        sql.Contains("FROM mensagens_whatsapp", StringComparison.Ordinal) && sql.Contains("LIMIT", StringComparison.Ordinal)
        && sql.Contains("AS \"Papel\"", StringComparison.Ordinal) && sql.Contains("m.conteudo", StringComparison.Ordinal);

    /// <summary>Observa os comandos SQL do EF: gancho assíncrono ANTES de executar cada consulta e registro do texto executado.</summary>
    private sealed class InterceptadorDeConsultas : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _comandos = new();

        public Func<string, Task>? AntesDe { get; set; }

        public IReadOnlyList<string> Comandos => _comandos.ToArray();

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _comandos.Enqueue(command.CommandText);
            var gancho = AntesDe;
            if (gancho is not null)
                await gancho(command.CommandText);
            return result;
        }
    }

    // ---------------------------------------------------------------- 1: limites dentro da rodada

    [Fact]
    public async Task Teto_de_chamadas_e_reavaliado_antes_de_cada_chamada_dentro_da_rodada()
    {
        await IniciarAsync(new() { ["Ai:LimiteChamadasPorNegocioPorDia"] = "2" });
        var c = await _ambiente.SemearAsync();
        // O modelo pede ferramenta para sempre: sem a reavaliação faria até 5 chamadas.
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Chama(("listar_servicos", "{}"));

        await _ambiente.EnviarAsync(c, "quero horários");

        _ambiente.Provedor.Chamadas.Should().Be(2, "o teto de 2 chamadas/dia vale também entre as voltas da mesma rodada");
        (await _ambiente.UsosAsync()).Should().HaveCount(2);
        _ambiente.UltimaEntrega().Should().Contain(c.Slug, "cai no fallback com o link");
    }

    [Fact]
    public async Task Cota_mensal_e_reavaliada_entre_as_chamadas_da_rodada()
    {
        // Cada chamada gasta 120 tokens (100 + 20): a cota de 200 deixa passar a 1ª (0) e a 2ª (120), e bloqueia a 3ª (240).
        await IniciarAsync(new() { ["Ai:CotaMensalTokensPorNegocio"] = "200" });
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Chama(("listar_servicos", "{}"));

        await _ambiente.EnviarAsync(c, "quero horários");

        _ambiente.Provedor.Chamadas.Should().Be(2);
    }

    // ---------------------------------------------------------------- 2: lease com dono

    [Fact]
    public async Task Lease_expirado_durante_a_IA_quem_perdeu_nao_envia_e_nao_solta_o_lease_de_quem_assumiu()
    {
        await IniciarAsync();
        var c = await _ambiente.SemearAsync();
        await _ambiente.EnviarAsync(c, "primeira"); // cria a conversa
        var conversa = (await _ambiente.ConversasAsync()).Single();
        _ambiente.Evolution.Envios.Clear();
        var chamadasAntes = _ambiente.Provedor.Chamadas;

        await _ambiente.NoBancoAsync(async db =>
        {
            db.MensagensWhatsApp.Add(new MensagemWhatsApp(c.NegocioId, conversa.InstanciaId, conversa.Id, "ID-L1", PapelMensagemWhatsApp.Usuario, "segunda"));
            return await db.SaveChangesAsync();
        });

        using var primeiraNaIa = new SemaphoreSlim(0);
        using var liberaPrimeira = new SemaphoreSlim(0);
        using var segundaNaIa = new SemaphoreSlim(0);
        using var liberaSegunda = new SemaphoreSlim(0);

        _ambiente.Provedor.Roteiro = (_, n) =>
        {
            if (n == chamadasAntes + 1)
            {
                primeiraNaIa.Release();
                liberaPrimeira.Wait(TimeSpan.FromSeconds(30));
                return ProvedorIaRoteirizado.Texto("resposta do processador A");
            }

            segundaNaIa.Release();
            liberaSegunda.Wait(TimeSpan.FromSeconds(30));
            return ProvedorIaRoteirizado.Texto("resposta do processador B");
        };

        var a = Task.Run(() => ProcessarAsync(conversa.Id));
        (await primeiraNaIa.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();

        // O lease do A expira (relógio simulado) e o B assume a conversa.
        await _ambiente.NoBancoAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE conversas_whatsapp SET processando_ate = now() - interval '1 minute'"));
        var b = Task.Run(() => ProcessarAsync(conversa.Id));
        (await segundaNaIa.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();

        // A termina primeiro: perdeu a posse, não envia nem solta o lease do B.
        liberaPrimeira.Release();
        await a;
        _ambiente.Evolution.Envios.Should().BeEmpty("quem perdeu o lease não envia");
        (await _ambiente.ConversasAsync()).Single().ProcessandoAte.Should().NotBeNull("o finally do A não limpa o lease do B");

        liberaSegunda.Release();
        await b;
        _ambiente.Evolution.Envios.Should().ContainSingle().Which.Texto.Should().Be("resposta do processador B");
        (await _ambiente.ConversasAsync()).Single().ProcessandoAte.Should().BeNull();
    }

    private async Task ProcessarAsync(Guid conversaId)
    {
        using var escopo = _ambiente.Fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<IProcessadorRodadaAgente>().ProcessarAsync(conversaId, CancellationToken.None);
    }

    // ---------------------------------------------------------------- 3: lote estável

    [Fact]
    public async Task Mensagem_que_chega_entre_a_captura_do_lote_e_a_IA_fica_de_fora_do_historico_e_e_respondida_na_rodada_seguinte()
    {
        await IniciarAsync();
        var c = await _ambiente.SemearAsync();
        await _ambiente.EnviarAsync(c, "primeira"); // cria a conversa
        var conversa = (await _ambiente.ConversasAsync()).Single();
        _ambiente.Evolution.Envios.Clear();
        var chamadasAntes = _ambiente.Provedor.Chamadas;

        await _ambiente.NoBancoAsync(async db =>
        {
            db.MensagensWhatsApp.Add(new MensagemWhatsApp(c.NegocioId, conversa.InstanciaId, conversa.Id, "ID-B1", PapelMensagemWhatsApp.Usuario, "mensagem do lote"));
            return await db.SaveChangesAsync();
        });

        // Chega DEPOIS de o processador capturar o lote (consulta das pendentes já executada) e ANTES de o orquestrador executar a
        // consulta do histórico: o interceptor dispara a inserção, uma única vez, exatamente na consulta do histórico.
        var inserida = false;
        _interceptador.AntesDe = async texto =>
        {
            if (inserida || !EhConsultaDoHistorico(texto))
                return;

            inserida = true;
            await AdicionarMensagemAsync(c, conversa, "ID-B2", "mensagem tardia");
        };
        _ambiente.Provedor.Roteiro = (_, n) => ProvedorIaRoteirizado.Texto("ok " + n);

        await ProcessarAsync(conversa.Id);
        inserida.Should().BeTrue("o interceptor precisa ter pegado a consulta do histórico");

        var requisicoes = _ambiente.Provedor.Requisicoes.Skip(chamadasAntes).ToList();
        requisicoes.Should().HaveCount(2, "a tardia exige exatamente uma rodada extra");
        // A 1ª rodada montou o histórico ANTES de a tardia existir (consulta única) e a 2ª já a inclui, uma só vez.
        var primeira = ProvedorIaRoteirizado.Serializar(requisicoes[0]);
        var segunda = ProvedorIaRoteirizado.Serializar(requisicoes[1]);
        primeira.Should().Contain("mensagem do lote").And.NotContain("mensagem tardia");
        segunda.Should().Contain("mensagem tardia");
        _ambiente.Evolution.Envios.Should().HaveCount(2);

        var pendentes = await _ambiente.NoBancoAsync(db => db.ConversasWhatsApp.IgnoreQueryFilters().AsNoTracking().Select(x => x.ProcessadaAte).SingleAsync());
        pendentes.Should().NotBeNull();
    }

    [Fact]
    public async Task Backlog_volumoso_responde_so_a_mais_recente_sem_materializar_as_pendentes()
    {
        await IniciarAsync();
        var c = await _ambiente.SemearAsync();
        await _ambiente.EnviarAsync(c, "primeira"); // cria a conversa
        var conversa = (await _ambiente.ConversasAsync()).Single();
        _ambiente.Evolution.Envios.Clear();
        var chamadasAntes = _ambiente.Provedor.Chamadas;

        await _ambiente.NoBancoAsync(async db =>
        {
            for (var i = 0; i < 300; i++)
                db.MensagensWhatsApp.Add(new MensagemWhatsApp(c.NegocioId, conversa.InstanciaId, conversa.Id, $"ID-BL{i}",
                    PapelMensagemWhatsApp.Usuario, i == 299 ? "ultima do backlog" : "antiga " + new string('x', 1500)));
            return await db.SaveChangesAsync();
        });
        _ambiente.Provedor.Roteiro = (_, n) => ProvedorIaRoteirizado.Texto("ok " + n);

        await ProcessarAsync(conversa.Id);

        _ambiente.Provedor.Chamadas.Should().Be(chamadasAntes + 1, "o backlog vira uma única rodada");
        ProvedorIaRoteirizado.Serializar(_ambiente.Provedor.Requisicoes[^1]).Should().Contain("ultima do backlog");
        _ambiente.Evolution.Envios.Should().ContainSingle();

        // Nenhuma consulta que lê o conteúdo das mensagens fica sem LIMIT (as pendentes não são carregadas por inteiro).
        var leiturasDeConteudo = _interceptador.Comandos.Where(s =>
            s.Contains("FROM mensagens_whatsapp", StringComparison.Ordinal) && s.Contains("m.conteudo", StringComparison.Ordinal)).ToList();
        leiturasDeConteudo.Should().NotBeEmpty().And.OnlyContain(s => s.Contains("LIMIT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Aviso_de_limite_aceito_pela_Evolution_com_timeout_nao_e_reenviado_na_mesma_janela()
    {
        await IniciarAsync(new() { ["Ai:LimiteChamadasPorNegocioPorDia"] = "1" });
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("ok");
        await _ambiente.EnviarAsync(c, "um"); // gasta a única chamada do dia

        // A Evolution "aceita" o aviso, mas a resposta nunca chega (timeout): o resultado é desconhecido.
        var tentativasDeAviso = 0;
        _ambiente.Evolution.AoEnviar = (_, texto, _) =>
        {
            if (!texto.Contains("pausar", StringComparison.Ordinal))
                return Task.CompletedTask;

            Interlocked.Increment(ref tentativasDeAviso);
            throw new TimeoutException("sem resposta");
        };

        await _ambiente.EnviarAsync(c, "dois");
        await _ambiente.EnviarAsync(c, "tres");
        await _ambiente.EnviarAsync(c, "quatro");

        tentativasDeAviso.Should().Be(1, "a reserva do aviso foi gravada antes do envio e continua suprimindo");
        (await _ambiente.MensagensAsync()).Count(m => m.Papel == PapelMensagemWhatsApp.Sistema && m.NomeFerramenta == "limite_aviso").Should().Be(1);
        (await _ambiente.ConversasAsync()).Single().ProcessandoAte.Should().BeNull();
    }

    // ---------------------------------------------------------------- 4: texto fixo sanitizado

    [Fact]
    public async Task Fallback_com_link_externo_no_nome_do_negocio_sai_sem_o_link_externo()
    {
        await IniciarAsync();
        var c = await _ambiente.SemearAsync(nome: "Barbearia golpe.ai/pix");
        _ambiente.Provedor.FalharComHttp500 = true;

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.UltimaEntrega().Should().NotContain("golpe").And.Contain(c.Slug);
    }

    [Fact]
    public async Task Aviso_de_limite_com_link_externo_no_nome_do_negocio_sai_sem_o_link_externo()
    {
        await IniciarAsync(new() { ["Ai:LimiteChamadasPorNegocioPorDia"] = "1" });
        var c = await _ambiente.SemearAsync(nome: "Loja https://evil.com/x bit.ly/y");
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("ok");

        await _ambiente.EnviarAsync(c, "um");
        await _ambiente.EnviarAsync(c, "dois"); // bate o teto -> aviso

        var aviso = _ambiente.UltimaEntrega()!;
        aviso.Should().Contain("pausar").And.Contain(c.Slug).And.NotContain("evil").And.NotContain("bit.ly");
    }

    // ================================================================ Volta 2

    [Fact]
    public async Task Nome_de_ferramenta_fora_do_catalogo_nunca_chega_ao_log_nem_ao_banco()
    {
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(new Dictionary<string, string?> { ["Ai:LimiteMensagensPorTelefonePorHora"] = "1000" }, capturarLogs: true);
        var c = await _ambiente.SemearAsync();
        const string sensivel = "5571988887777_cpf_12345678909";
        _ambiente.Provedor.Roteiro = (_, n) => n == 1
            ? ProvedorIaRoteirizado.Chama((sensivel, "{}"), ("listar_servicos", "{}"))
            : ProvedorIaRoteirizado.Texto("ok");

        await _ambiente.EnviarAsync(c, "oi");

        var logs = string.Join('\n', _ambiente.Logs!.Linhas);
        logs.Should().NotContain("5571988887777").And.NotContain("12345678909").And.NotContain(sensivel);
        logs.Should().Contain("listar_servicos", "nome do catálogo continua sendo registrado");
        var marcadores = (await _ambiente.MensagensAsync()).Where(m => m.Papel == PapelMensagemWhatsApp.Ferramenta).Select(m => m.NomeFerramenta).ToList();
        marcadores.Should().BeEquivalentTo(["ferramenta_desconhecida", "listar_servicos"]);
    }

    [Fact]
    public async Task Historico_menor_que_uma_mensagem_trunca_a_primeira_ao_teto()
    {
        await IniciarAsync(new() { ["Ai:MaxCaracteresHistorico"] = "200", ["Ai:MaxCaracteresMensagem"] = "1000" });
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("ok");

        await _ambiente.EnviarAsync(c, new string('a', 900));

        var mensagens = _ambiente.Provedor.Requisicoes.Single().Mensagens;
        mensagens.Should().ContainSingle();
        mensagens[0].Texto!.Length.Should().BeLessThanOrEqualTo(200);
        mensagens[0].Texto.Should().StartWith("aaa");
    }

    [Fact]
    public async Task Historico_com_varias_mensagens_conta_separadores_e_nunca_passa_do_teto()
    {
        await IniciarAsync(new() { ["Ai:MaxCaracteresHistorico"] = "200" });
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("ok");

        await _ambiente.EnviarAsync(c, new string('b', 100));
        await _ambiente.EnviarAsync(c, new string('c', 100)); // histórico: b(100) + "ok" + c(100) > 200

        var ultima = _ambiente.Provedor.Requisicoes[^1].Mensagens;
        ultima.Sum(m => m.Texto!.Length).Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task Envio_bloqueado_alem_do_prazo_do_lease_e_abandonado_e_nao_atropela_quem_assumiu()
    {
        var duracaoAntes = ProcessadorRodadaAgente.DuracaoEnvio;
        var margemAntes = ProcessadorRodadaAgente.MargemEnvio;
        ProcessadorRodadaAgente.DuracaoEnvio = TimeSpan.FromSeconds(4);
        ProcessadorRodadaAgente.MargemEnvio = TimeSpan.FromSeconds(1);
        try
        {
            await IniciarAsync();
            var c = await _ambiente.SemearAsync();
            await _ambiente.EnviarAsync(c, "primeira"); // cria a conversa
            var conversa = (await _ambiente.ConversasAsync()).Single();
            _ambiente.Evolution.Envios.Clear();
            await AdicionarMensagemAsync(c, conversa, "ID-E1", "segunda");

            using var entrouNoEnvio = new SemaphoreSlim(0);
            var cancelado = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var primeiro = true;
            _ambiente.Evolution.AoEnviar = async (_, _, ct) =>
            {
                if (!primeiro)
                    return;

                primeiro = false;
                entrouNoEnvio.Release();
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { cancelado.TrySetResult(true); throw; }
            };
            _ambiente.Provedor.Roteiro = (_, n) => ProvedorIaRoteirizado.Texto("resposta " + n);

            var a = Task.Run(() => ProcessarAsync(conversa.Id));
            (await entrouNoEnvio.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();

            // O lease do A vence "de fora" e o B assume, com uma mensagem nova para responder.
            await _ambiente.NoBancoAsync(db => db.Database.ExecuteSqlRawAsync(
                "UPDATE conversas_whatsapp SET processando_ate = now() - interval '1 minute'"));
            await AdicionarMensagemAsync(c, conversa, "ID-E2", "terceira");
            await ProcessarAsync(conversa.Id);

            (await cancelado.Task.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue("o envio do A é cortado pelo prazo do lease");
            await a;

            _ambiente.Evolution.Envios.Should().ContainSingle("só o B entrega; o A abandonou o envio").Which.Texto.Should().StartWith("resposta");
            (await _ambiente.MensagensAsync()).Count(m => m.Papel == PapelMensagemWhatsApp.Assistente).Should().Be(2, "a resposta da 1ª mensagem + a do B; nada do A");
            (await _ambiente.ConversasAsync()).Single().ProcessandoAte.Should().BeNull();
        }
        finally
        {
            ProcessadorRodadaAgente.DuracaoEnvio = duracaoAntes;
            ProcessadorRodadaAgente.MargemEnvio = margemAntes;
        }
    }

    [Fact]
    public async Task Aviso_de_limite_bloqueado_alem_do_prazo_do_lease_e_abandonado_e_a_conversa_nao_fica_presa()
    {
        var duracaoAntes = ProcessadorRodadaAgente.DuracaoEnvio;
        var margemAntes = ProcessadorRodadaAgente.MargemEnvio;
        ProcessadorRodadaAgente.DuracaoEnvio = TimeSpan.FromSeconds(4);
        ProcessadorRodadaAgente.MargemEnvio = TimeSpan.FromSeconds(1);
        try
        {
            await IniciarAsync(new() { ["Ai:LimiteChamadasPorNegocioPorDia"] = "1" });
            var c = await _ambiente.SemearAsync();
            _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("ok");
            await _ambiente.EnviarAsync(c, "um"); // gasta a única chamada do dia

            var cancelado = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ambiente.Evolution.AoEnviar = async (_, _, ct) =>
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { cancelado.TrySetResult(true); throw; }
            };

            await _ambiente.EnviarAsync(c, "dois"); // bate o teto -> aviso, bloqueado até o prazo do lease

            cancelado.Task.IsCompleted.Should().BeTrue("o aviso foi cortado pelo prazo do lease");
            _ambiente.Evolution.Envios.Should().ContainSingle("só a resposta da 1ª mensagem; o aviso foi abandonado");
            var conversa = (await _ambiente.ConversasAsync()).Single();
            conversa.ProcessandoAte.Should().BeNull();
            conversa.ProcessadaAte.Should().NotBeNull("o lote foi marcado (com posse conferida) antes do aviso");
        }
        finally
        {
            ProcessadorRodadaAgente.DuracaoEnvio = duracaoAntes;
            ProcessadorRodadaAgente.MargemEnvio = margemAntes;
        }
    }

    private Task<int> AdicionarMensagemAsync(CenarioAgente c, ConversaWhatsApp conversa, string idExterno, string texto) =>
        _ambiente.NoBancoAsync(async db =>
        {
            db.MensagensWhatsApp.Add(new MensagemWhatsApp(c.NegocioId, conversa.InstanciaId, conversa.Id, idExterno, PapelMensagemWhatsApp.Usuario, texto));
            return await db.SaveChangesAsync();
        });

    // ---------------------------------------------------------------- 5: minimização

    [Fact]
    public async Task Dado_pessoal_escrito_pelo_cliente_nao_vai_para_a_IA_nem_na_mensagem_atual_nem_no_historico()
    {
        await IniciarAsync();
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => ProvedorIaRoteirizado.Texto("ok");

        await _ambiente.EnviarAsync(c, "meu e-mail é cliente@exemplo.com, cpf 123.456.789-09 e outro fone 11 97777-6666");
        await _ambiente.EnviarAsync(c, "e quais horários amanhã?");

        _ambiente.Provedor.Chamadas.Should().Be(2);
        foreach (var requisicao in _ambiente.Provedor.Requisicoes)
        {
            var tudo = ProvedorIaRoteirizado.Serializar(requisicao);
            tudo.Should().NotContain("cliente@exemplo.com").And.NotContain("123.456.789-09").And.NotContain("97777")
                .And.NotContain(AmbienteAgente.TelefoneCliente);
        }

        ProvedorIaRoteirizado.Serializar(_ambiente.Provedor.Requisicoes[1]).Should().Contain("removido", "o histórico segue, já minimizado");
    }
}
