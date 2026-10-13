using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Aplicacao.Idempotencia;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Idempotencia;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using Xunit.Abstractions;

namespace Plataforma.Testes.Integracao.Idempotencia;

/// <summary>
/// Idempotência Fase 1 — comportamentos transversais: espera máxima (409 + Retry-After, sem retry da estratégia), classificação do
/// 55P03 no Npgsql, limpeza, isolamento por negócio, limite do resultado, CORS, configuração (desligar/obrigar) e a migration
/// (Up e Down em banco descartável).
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class IdempotenciaComportamentoTestes
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly PostgresContainerFixture _postgres;
    private readonly ITestOutputHelper _saida;

    public IdempotenciaComportamentoTestes(PostgresContainerFixture postgres, ITestOutputHelper saida)
    {
        _postgres = postgres;
        _saida = saida;
    }

    // ------------------------------------------------------------------ espera máxima

    [Fact]
    public async Task Duplicado_que_espera_alem_do_limite_recebe_409_com_retry_after_e_sem_retry_da_estrategia()
    {
        await using var fabrica = NovaFabrica(new Dictionary<string, string?> { ["Idempotencia:EsperaMaximaSegundos"] = "1" });
        var (admin, negocioId, produto, profissional) = await ArranjarAsync(fabrica);
        var corpo = new LancarVenda([new ItemLancarVenda(produto, 1, 20m)], profissional, null);
        var chave = Guid.NewGuid().ToString();

        await using var trava = await TravaExterna.AbrirAsync(_postgres.ConnectionString, "SELECT 1 FROM produtos WHERE id = @id FOR UPDATE", produto);
        var primeira = Enviar(admin, "/painel/vendas", corpo, chave);
        (await EsperarBloqueadosAsync(1, primeira)).Should().BeTrue("a 1ª reivindicou a chave e espera a trava do produto");

        var relogio = Stopwatch.StartNew();
        var segunda = await Enviar(admin, "/painel/vendas", corpo, chave);
        relogio.Stop();

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
        segunda.Headers.GetValues("Retry-After").Should().ContainSingle().Which.Should().Be("2");
        var problema = await segunda.Content.ReadFromJsonAsync<JsonElement>();
        problema.GetProperty("codigo").GetString().Should().Be("operacao_em_andamento");
        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4), "uma única espera de ~1 s: 55P03 não é repetida pela estratégia (5 tentativas levariam mais)");

        await trava.LiberarAsync();
        (await primeira).StatusCode.Should().Be(HttpStatusCode.Created);
        (await fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().CountAsync(v => v.NegocioId == negocioId))).Should().Be(1);

        // Depois que a 1ª confirma, a mesma chave devolve o resultado (não 409).
        var terceira = await Enviar(admin, "/painel/vendas", corpo, chave);
        terceira.StatusCode.Should().Be(HttpStatusCode.Created);
        terceira.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
    }

    [Fact]
    public async Task Npgsql_classifica_55P03_e_o_codigo_a_trata_como_nao_transitoria()
    {
        await using var a = new NpgsqlConnection(_postgres.ConnectionString);
        await using var b = new NpgsqlConnection(_postgres.ConnectionString);
        await a.OpenAsync();
        await b.OpenAsync();
        await using (var segura = new NpgsqlCommand("SELECT pg_advisory_lock(424242)", a))
            await segura.ExecuteNonQueryAsync();
        await using (var limite = new NpgsqlCommand("SET lock_timeout = '300ms'", b))
            await limite.ExecuteNonQueryAsync();

        var acao = async () =>
        {
            await using var espera = new NpgsqlCommand("SELECT pg_advisory_lock(424242)", b);
            await espera.ExecuteNonQueryAsync();
        };

        var excecao = (await acao.Should().ThrowAsync<PostgresException>()).Which;
        excecao.SqlState.Should().Be("55P03");
        _saida.WriteLine($"B8|PostgresException 55P03 IsTransient={excecao.IsTransient}");
        await using (var solta = new NpgsqlCommand("SELECT pg_advisory_unlock(424242)", a))
            await solta.ExecuteNonQueryAsync();
    }

    // ------------------------------------------------------------------ limpeza, tenant e limite

    [Fact]
    public async Task Limpeza_remove_so_os_expirados_de_todos_os_negocios_em_lotes()
    {
        await using var fabrica = NovaFabrica();
        var (_, negocioA, _, _) = await ArranjarAsync(fabrica);
        var (_, negocioB, _, _) = await ArranjarAsync(fabrica);
        const int expiradosPorNegocio = 1300; // 2.600 no total: mais de 2 lotes de 1.000
        foreach (var negocio in new[] { negocioA, negocioB })
        {
            await Executar($@"
                INSERT INTO requisicoes_idempotentes (id, negocio_id, principal, escopo, chave, hash_requisicao, status_http, resultado, criado_em, expira_em)
                SELECT gen_random_uuid(), '{negocio}', 'u:x', 'POST painel/vendas', 'expirada-' || g, 'h', 201, '{{}}'::jsonb, now() - interval '2 days', now() - interval '1 hour'
                FROM generate_series(1, {expiradosPorNegocio}) g;
                INSERT INTO requisicoes_idempotentes (id, negocio_id, principal, escopo, chave, hash_requisicao, status_http, resultado, criado_em, expira_em)
                VALUES (gen_random_uuid(), '{negocio}', 'u:x', 'POST painel/vendas', 'vigente-1', 'h', 201, '{{}}'::jsonb, now(), now() + interval '2 hours');");
        }

        int removidas;
        using (var escopo = fabrica.Services.CreateScope())
            removidas = await escopo.ServiceProvider.GetRequiredService<JobLimparIdempotencia>().ExecutarAsync();

        removidas.Should().Be(2 * expiradosPorNegocio);
        var restantes = await fabrica.NoBancoAsync(db => db.RequisicoesIdempotentes.IgnoreQueryFilters()
            .Where(r => r.NegocioId == negocioA || r.NegocioId == negocioB).Select(r => r.Chave).ToListAsync());
        restantes.Should().BeEquivalentTo(["vigente-1", "vigente-1"], "só os não expirados sobram, nos dois negócios");
    }

    [Fact]
    public async Task Tabela_so_e_visivel_dentro_do_proprio_negocio()
    {
        await using var fabrica = NovaFabrica();
        var (adminA, negocioA, produtoA, profA) = await ArranjarAsync(fabrica);
        var (adminB, negocioB, produtoB, profB) = await ArranjarAsync(fabrica);
        (await Enviar(adminA, "/painel/vendas", new LancarVenda([new ItemLancarVenda(produtoA, 1, 20m)], profA, null), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        (await Enviar(adminB, "/painel/vendas", new LancarVenda([new ItemLancarVenda(produtoB, 1, 20m)], profB, null), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();

        using var escopo = fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioA);
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var visiveis = await db.RequisicoesIdempotentes.AsNoTracking().Select(r => r.NegocioId).ToListAsync();

        visiveis.Should().ContainSingle().Which.Should().Be(negocioA, "o filtro global esconde as chaves do outro negócio");
        (await db.RequisicoesIdempotentes.IgnoreQueryFilters().CountAsync(r => r.NegocioId == negocioB)).Should().Be(1);
    }

    [Fact]
    public async Task Resultado_acima_de_8_kb_e_recusado_pelo_servico_e_pelo_check_do_banco()
    {
        await using var fabrica = NovaFabrica();
        var (_, negocioId, _, _) = await ArranjarAsync(fabrica);
        var usuarioId = await fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().Where(u => u.NegocioId == negocioId).Select(u => u.Id).FirstAsync());

        using (var escopo = fabrica.Services.CreateScope())
        {
            var sp = escopo.ServiceProvider;
            sp.GetRequiredService<IContextoNegocio>().Definir(negocioId);
            sp.GetRequiredService<IUsuarioAtual>().Definir(usuarioId, "x@teste.com");
            sp.GetRequiredService<ContextoIdempotencia>().Definir(Guid.NewGuid().ToString(), "POST painel/teste", "hash", 201);
            var db = sp.GetRequiredService<PlataformaDbContext>();
            var servico = sp.GetRequiredService<IServicoIdempotencia>();

            await using var transacao = await db.Database.BeginTransactionAsync();
            (await servico.ReivindicarAsync<ResultadoGrande>(default)).Should().BeNull();
            var acao = () => servico.ConcluirAsync(new ResultadoGrande(new string('x', 9000)), default);
            await acao.Should().ThrowAsync<InvalidOperationException>().WithMessage("*acima do limite*");
        }

        var direto = () => Executar($@"
            INSERT INTO requisicoes_idempotentes (id, negocio_id, principal, escopo, chave, hash_requisicao, status_http, resultado, criado_em, expira_em)
            VALUES (gen_random_uuid(), '{negocioId}', 'u:x', 'e', 'k', 'h', 201, to_jsonb(repeat('x', 9000)), now(), now() + interval '1 hour')");
        (await direto.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514", "o CHECK barra resultado grande mesmo fora do serviço");
    }

    private sealed record ResultadoGrande(string Texto);

    // ------------------------------------------------------------------ CORS

    [Fact]
    public async Task Cors_expoe_idempotent_replayed_e_retry_after_ao_painel_em_outro_host()
    {
        // O Program.cs lê Marca:Dominio cedo (antes do override de configuração das fábricas), então a política de CORS só nasce com o
        // domínio vindo do ambiente. Valor idêntico ao de todos os testes; restaurado logo após o host subir.
        using var cliente = CriarClienteComCors(out var fabrica);
        await using var _ = fabrica;
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/health");
        requisicao.Headers.Add("Origin", $"https://app.{DominioDeTeste.Valor}");

        var resposta = await cliente.SendAsync(requisicao);

        resposta.Headers.GetValues("Access-Control-Expose-Headers").SelectMany(v => v.Split(',')).Select(v => v.Trim())
            .Should().Contain(["Idempotent-Replayed", "Retry-After"]);
    }

    private HttpClient CriarClienteComCors(out PlataformaWebApplicationFactory fabrica)
    {
        var anterior = Environment.GetEnvironmentVariable("Marca__Dominio");
        Environment.SetEnvironmentVariable("Marca__Dominio", DominioDeTeste.Valor);
        try
        {
            fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
            return fabrica.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Marca__Dominio", anterior);
        }
    }

    // ------------------------------------------------------------------ configuração

    [Fact]
    public async Task Mecanismo_desligado_ignora_o_cabecalho_e_volta_ao_comportamento_anterior()
    {
        await using var fabrica = NovaFabrica(new Dictionary<string, string?> { ["Idempotencia:Habilitada"] = "false" });
        var (admin, negocioId, produto, profissional) = await ArranjarAsync(fabrica);
        var corpo = new LancarVenda([new ItemLancarVenda(produto, 1, 20m)], profissional, null);
        var chave = Guid.NewGuid().ToString();

        var primeira = await Enviar(admin, "/painel/vendas", corpo, chave);
        var segunda = await Enviar(admin, "/painel/vendas", corpo, chave);

        primeira.StatusCode.Should().Be(HttpStatusCode.Created);
        segunda.StatusCode.Should().Be(HttpStatusCode.Created);
        segunda.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        (await fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().CountAsync(v => v.NegocioId == negocioId))).Should().Be(2);
        (await fabrica.NoBancoAsync(db => db.RequisicoesIdempotentes.IgnoreQueryFilters().CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task Chave_obrigatoria_quando_ligada_recusa_sem_cabecalho_e_aceita_com_ele()
    {
        await using var fabrica = NovaFabrica(new Dictionary<string, string?> { ["Idempotencia:Obrigatoria"] = "true" });
        var (admin, _, produto, profissional) = await ArranjarAsync(fabrica);
        var corpo = new LancarVenda([new ItemLancarVenda(produto, 1, 20m)], profissional, null);

        var sem = await admin.PostAsJsonAsync("/painel/vendas", corpo);
        var com = await Enviar(admin, "/painel/vendas", corpo, Guid.NewGuid().ToString());

        sem.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await sem.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("idempotency_key_obrigatoria");
        com.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Padroes_seguros_habilitada_opcional_24h_e_10s()
    {
        await using var fabrica = NovaFabrica();
        var opcoes = fabrica.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Plataforma.Infraestrutura.Opcoes.OpcoesIdempotencia>>().Value;

        opcoes.Habilitada.Should().BeTrue();
        opcoes.Obrigatoria.Should().BeFalse();
        opcoes.RetencaoHoras.Should().Be(24);
        opcoes.EsperaMaximaSegundos.Should().Be(10);
    }

    // ------------------------------------------------------------------ migration

    [Fact]
    public async Task Migration_sobe_cria_a_tabela_com_indices_fk_e_check_e_desce_sem_tocar_nas_outras()
    {
        var banco = $"idem_migracao_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var criar = new NpgsqlCommand($"CREATE DATABASE {banco}", admin);
            await criar.ExecuteNonQueryAsync();
        }

        var cs = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString) { Database = banco, Pooling = false }.ConnectionString;
        try
        {
            await using var fabrica = new PlataformaWebApplicationFactory(cs);
            using var aquecimento = fabrica.CreateClient(); // Development aplica as migrations pendentes ao subir (Up)

            (await Consultar<string>(cs, "SELECT migration_id FROM \"__EFMigrationsHistory\" WHERE migration_id LIKE '%RequisicoesIdempotentes'"))
                .Should().ContainSingle();
            (await Consultar<string>(cs, "SELECT column_name FROM information_schema.columns WHERE table_name = 'requisicoes_idempotentes' ORDER BY column_name"))
                .Should().BeEquivalentTo(["atualizado_em", "chave", "criado_em", "escopo", "expira_em", "hash_requisicao", "id", "negocio_id", "principal", "resultado", "status_http"]);
            (await Consultar<string>(cs, "SELECT indexdef FROM pg_indexes WHERE tablename = 'requisicoes_idempotentes'"))
                .Should().Contain(d => d.Contains("UNIQUE") && d.Contains("negocio_id, principal, escopo, chave"))
                .And.Contain(d => d.Contains("(expira_em)"));
            (await Consultar<string>(cs, "SELECT conname FROM pg_constraint WHERE conrelid = 'requisicoes_idempotentes'::regclass ORDER BY conname"))
                .Should().Contain(["ck_requisicoes_idempotentes_resultado_tamanho", "fk_requisicoes_idempotentes_negocios_negocio_id", "pk_requisicoes_idempotentes"]);
            (await Consultar<string>(cs, "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'fk_requisicoes_idempotentes_negocios_negocio_id'"))
                .Should().ContainSingle().Which.Should().Be("c", "cascade ao apagar o negócio");

            var tabelasAntes = await Consultar<string>(cs, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> 'requisicoes_idempotentes' ORDER BY table_name");

            // Down: volta para a migration anterior (LembreteRetorno).
            using (var escopo = fabrica.Services.CreateScope())
            {
                var migrador = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().GetService<IMigrator>();
                await migrador.MigrateAsync("20261011222815_LembreteRetorno");
            }

            (await Consultar<string>(cs, "SELECT table_name FROM information_schema.tables WHERE table_name = 'requisicoes_idempotentes'")).Should().BeEmpty();
            (await Consultar<string>(cs, "SELECT migration_id FROM \"__EFMigrationsHistory\" WHERE migration_id LIKE '%RequisicoesIdempotentes'")).Should().BeEmpty();
            (await Consultar<string>(cs, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name"))
                .Should().BeEquivalentTo(tabelasAntes, "o Down só remove a tabela desta fase");

            // Up de novo: idempotente em banco que já passou por ele.
            using (var escopo = fabrica.Services.CreateScope())
                await escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().Database.MigrateAsync();
            (await Consultar<string>(cs, "SELECT table_name FROM information_schema.tables WHERE table_name = 'requisicoes_idempotentes'")).Should().ContainSingle();
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_postgres.ConnectionString);
            await admin.OpenAsync();
            await using var apagar = new NpgsqlCommand($"DROP DATABASE IF EXISTS {banco} WITH (FORCE)", admin);
            await apagar.ExecuteNonQueryAsync();
        }
    }

    // ------------------------------------------------------------------ apoio

    private PlataformaWebApplicationFactory NovaFabrica(IReadOnlyDictionary<string, string?>? configuracao = null)
    {
        var fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, configuracao);
        using var aquecimento = fabrica.CreateClient();
        fabrica.ResetarBancoAsync().GetAwaiter().GetResult();
        return fabrica;
    }

    private static async Task<(HttpClient Admin, Guid NegocioId, Guid Produto, Guid Profissional)> ArranjarAsync(PlataformaWebApplicationFactory fabrica)
    {
        var (admin, negocioId, _, _) = await fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var agenda = await fabrica.CriarCenarioPadraoAsync(negocioId);
        var criado = await admin.PostAsJsonAsync("/painel/estoque/produtos", new CriarProduto("Pomada Idem", null, 5m, 20m, 100));
        criado.EnsureSuccessStatusCode();
        return (admin, negocioId, await criado.Content.ReadFromJsonAsync<Guid>(), agenda.ProfissionalId);
    }

    private static async Task<HttpResponseMessage> Enviar(HttpClient cliente, string url, object corpo, string chave)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo, options: Web) };
        requisicao.Headers.Add("Idempotency-Key", chave);
        return await cliente.SendAsync(requisicao);
    }

    private async Task Executar(string sql)
    {
        await using var conexao = new NpgsqlConnection(_postgres.ConnectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task<List<T>> Consultar<T>(string connectionString, string sql)
    {
        await using var conexao = new NpgsqlConnection(connectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        var itens = new List<T>();
        await using var leitor = await comando.ExecuteReaderAsync();
        while (await leitor.ReadAsync())
            itens.Add(leitor.GetFieldValue<T>(0));
        return itens;
    }

    private async Task<bool> EsperarBloqueadosAsync(int quantos, params Task[] concorrentes)
    {
        await using var conexao = new NpgsqlConnection(_postgres.ConnectionString);
        await conexao.OpenAsync();
        for (var i = 0; i < 4000 && !concorrentes.Any(t => t.IsCompleted); i++)
        {
            await using var consulta = new NpgsqlCommand("SELECT count(DISTINCT pid) FROM pg_locks WHERE NOT granted", conexao);
            if ((long)(await consulta.ExecuteScalarAsync())! >= quantos)
                return true;
        }

        return false;
    }

    private sealed class TravaExterna : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conexao;
        private readonly NpgsqlTransaction _transacao;
        private bool _encerrada;

        private TravaExterna(NpgsqlConnection conexao, NpgsqlTransaction transacao)
        {
            _conexao = conexao;
            _transacao = transacao;
        }

        public static async Task<TravaExterna> AbrirAsync(string connectionString, string sql, Guid id)
        {
            var conexao = new NpgsqlConnection(connectionString);
            await conexao.OpenAsync();
            var transacao = await conexao.BeginTransactionAsync();
            await using var comando = new NpgsqlCommand(sql, conexao, transacao);
            comando.Parameters.AddWithValue("id", id);
            await comando.ExecuteNonQueryAsync();
            return new TravaExterna(conexao, transacao);
        }

        public async Task LiberarAsync()
        {
            await _transacao.CommitAsync();
            _encerrada = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_encerrada)
                await _transacao.RollbackAsync();
            await _transacao.DisposeAsync();
            await _conexao.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
        }
    }
}

/// <summary>
/// Caso real: o proxy TCP derruba a conexão antes de a resposta do COMMIT voltar, com <c>Idempotency-Key</c>. A repetição da
/// estratégia reivindica a chave de novo, encontra a linha confirmada e devolve o mesmo resultado — uma venda, uma chave.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class IdempotenciaProxyTestes : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly PostgresContainerFixture _postgres;
    private ProxyQuePerdeResposta _proxy = null!;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public IdempotenciaProxyTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        var real = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString);
        _proxy = new ProxyQuePerdeResposta(real.Host!, real.Port);
        var viaProxy = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString) { Host = "127.0.0.1", Port = _proxy.Porta, Pooling = false }.ConnectionString;
        _fabrica = new PlataformaWebApplicationFactory(viaProxy);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync()
    {
        await _fabrica.DisposeAsync();
        _proxy.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    [Fact]
    public async Task Venda_com_chave_e_conexao_derrubada_no_commit_devolve_o_mesmo_resultado_e_grava_uma_vez()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var agenda = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var criado = await admin.PostAsJsonAsync("/painel/estoque/produtos", new CriarProduto("Pomada Proxy Idem", null, 5m, 20m, 10));
        var produto = await criado.Content.ReadFromJsonAsync<Guid>();
        var corpo = new LancarVenda([new ItemLancarVenda(produto, 2, 20m)], agenda.ProfissionalId, null);
        var chave = Guid.NewGuid().ToString();

        _proxy.Armar("INSERT INTO requisicoes_idempotentes", "COMMIT");
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/painel/vendas") { Content = JsonContent.Create(corpo, options: Web) };
        requisicao.Headers.Add("Idempotency-Key", chave);
        var primeira = await admin.SendAsync(requisicao);

        _proxy.Derrubadas.Should().Be(1, "a conexão da venda precisa ter sido derrubada no COMMIT");
        primeira.StatusCode.Should().Be(HttpStatusCode.Created);
        var lancada = (await primeira.Content.ReadFromJsonAsync<VendaLancada>())!;
        lancada.Total.Should().Be(40m);

        using var repeticao = new HttpRequestMessage(HttpMethod.Post, "/painel/vendas") { Content = JsonContent.Create(corpo, options: Web) };
        repeticao.Headers.Add("Idempotency-Key", chave);
        var segunda = await admin.SendAsync(repeticao);
        segunda.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
        (await segunda.Content.ReadFromJsonAsync<VendaLancada>()).Should().Be(lancada);

        (await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().CountAsync(v => v.NegocioId == negocioId))).Should().Be(1);
        (await _fabrica.NoBancoAsync(db => db.RequisicoesIdempotentes.IgnoreQueryFilters().CountAsync(r => r.NegocioId == negocioId))).Should().Be(1);
        (await _fabrica.NoBancoAsync(db => db.Produtos.IgnoreQueryFilters().Where(p => p.Id == produto).Select(p => p.QuantidadeEstoque).SingleAsync())).Should().Be(8);
    }
}
