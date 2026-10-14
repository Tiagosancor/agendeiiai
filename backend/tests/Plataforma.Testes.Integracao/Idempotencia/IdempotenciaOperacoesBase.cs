using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Dominio.Estoque;
using Plataforma.Dominio.Comissoes;
using Plataforma.Dominio.Fidelidade;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Idempotencia;

/// <summary>
/// Idempotência (Fases 1 e 3A): <c>Idempotency-Key</c> nos 7 endpoints do painel (venda, entrada, consumo, vale, agendamento
/// forçado, encaixe e resgate de fidelidade). A chave, o efeito e o resultado confirmam na mesma transação; o mesmo contrato vale com a chave opcional e com a chave obrigatória (classes derivadas).
/// Parametrizado por endpoint. Concorrência coordenada por trava externa e <c>pg_locks</c> — sem <c>Task.Delay</c>.
/// </summary>

public abstract class IdempotenciaOperacoesBase : IAsyncLifetime
{
    private const string EmailProf = "prof.idem@teste.com";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    protected readonly PostgresContainerFixture _postgres;
    private readonly bool _obrigatoria;
    private readonly InterceptadorDeCommit _commit = new();
    protected PlataformaWebApplicationFactory _fabrica = null!;

    protected IdempotenciaOperacoesBase(PostgresContainerFixture postgres, bool obrigatoria)
    {
        _postgres = postgres;
        _obrigatoria = obrigatoria;
    }

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, new Dictionary<string, string?> { ["Idempotencia:Obrigatoria"] = _obrigatoria ? "true" : "false" }, servicos =>
            servicos.ConfigureDbContext<PlataformaDbContext>(o => o.AddInterceptors(_commit)));
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync()
    {
        await _fabrica.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    public static IEnumerable<object[]> Operacoes() =>
        new[] { "venda", "entrada", "consumo", "vale", "forcado", "encaixe", "resgate" }.Select(n => new object[] { n });

    // ------------------------------------------------------------------ contrato HTTP

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Primeira_requisicao_executa_uma_vez_e_replay_devolve_mesmo_status_e_corpo_com_a_marca(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);
        var chave = NovaChave();

        var primeira = await Enviar(c, op, chave);
        var corpoPrimeira = await primeira.Content.ReadAsStringAsync();
        (await op.Efeitos(this, c)).Should().Be(1);
        primeira.StatusCode.Should().Be(op.Status);
        primeira.Headers.Contains("Idempotent-Replayed").Should().BeFalse();

        var segunda = await Enviar(c, op, chave);

        segunda.StatusCode.Should().Be(op.Status);
        (await segunda.Content.ReadAsStringAsync()).Should().Be(corpoPrimeira);
        segunda.Headers.GetValues("Idempotent-Replayed").Should().ContainSingle().Which.Should().Be("true");
        (await op.Efeitos(this, c)).Should().Be(1, "o replay não reaplica o efeito");
        (await RegistrosAsync(c)).Should().Be(1);
    }

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Mesmo_corpo_com_formatacao_e_ordem_diferentes_e_replay(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);
        if (op.SemCorpo)
            return;
        var chave = NovaChave();

        (await Enviar(c, op, chave)).StatusCode.Should().Be(op.Status);
        var reformatada = await Enviar(c, op, chave, formatoAlternativo: true);

        reformatada.StatusCode.Should().Be(op.Status);
        reformatada.Headers.GetValues("Idempotent-Replayed").Should().ContainSingle();
        (await op.Efeitos(this, c)).Should().Be(1);
    }

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Mesma_chave_com_corpo_diferente_responde_422_com_codigo_estavel(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);
        var chave = NovaChave();

        (await Enviar(c, op, chave)).StatusCode.Should().Be(op.Status);
        var outro = await Enviar(c, op, chave, variante: 1);

        outro.StatusCode.Should().Be((HttpStatusCode)422);
        var problema = await outro.Content.ReadFromJsonAsync<JsonElement>();
        problema.GetProperty("codigo").GetString().Should().Be("idempotency_key_reutilizada");
        problema.GetProperty("title").GetString().Should().NotContain(chave, "a mensagem é segura");
        (await op.Efeitos(this, c)).Should().Be(1);
    }

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Chave_invalida_responde_400_e_nada_e_executado(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);

        foreach (var invalida in new[] { "curta", "tem espaço e tem mais de dezesseis", new string('a', 101), "chave!com!simbolos!invalidos" })
        {
            var resposta = await Enviar(c, op, invalida);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, invalida);
            (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("idempotency_key_invalida");
        }

        using var duas = Requisicao(c, op, NovaChave(), 0, false);
        duas.Headers.Add("Idempotency-Key", NovaChave());
        (await c.Admin.SendAsync(duas)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "dois cabeçalhos são ambíguos");

        (await op.Efeitos(this, c)).Should().Be(0);
        (await RegistrosAsync(c)).Should().Be(0);
    }

    // ------------------------------------------------------------------ escopo

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Outro_usuario_do_mesmo_negocio_nao_colide(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);
        var chave = NovaChave();
        var outroUsuario = await _fabrica.LogarNovoUsuarioAsync(c.NegocioId, Perfil.Administrador);

        (await Enviar(c, op, chave)).StatusCode.Should().Be(op.Status);
        var doOutro = await Enviar(c, op, chave, cliente: outroUsuario);

        doOutro.StatusCode.Should().Be(op.Status);
        doOutro.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        (await op.Efeitos(this, c)).Should().Be(2);
        (await RegistrosAsync(c)).Should().Be(2);
    }

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Outro_negocio_nao_colide_nem_enxerga(string nome)
    {
        var a = await ArranjarAsync();
        var b = await ArranjarAsync();
        var op = Op(nome);
        var chave = NovaChave();

        (await Enviar(a, op, chave)).StatusCode.Should().Be(op.Status);
        var doOutro = await Enviar(b, op, chave);

        doOutro.StatusCode.Should().Be(op.Status);
        doOutro.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        (await op.Efeitos(this, a)).Should().Be(1);
        (await op.Efeitos(this, b)).Should().Be(1);
        (await RegistrosAsync(a)).Should().Be(1);
        (await RegistrosAsync(b)).Should().Be(1);
    }

    [Fact]
    public async Task Outro_endpoint_com_a_mesma_chave_nao_colide()
    {
        var c = await ArranjarAsync();
        var chave = NovaChave();

        var venda = await Enviar(c, Op("venda"), chave);
        var entrada = await Enviar(c, Op("entrada"), chave);
        var vale = await Enviar(c, Op("vale"), chave);

        venda.StatusCode.Should().Be(HttpStatusCode.Created);
        entrada.StatusCode.Should().Be(HttpStatusCode.OK);
        vale.StatusCode.Should().Be(HttpStatusCode.Created);
        new[] { venda, entrada, vale }.Should().OnlyContain(r => !r.Headers.Contains("Idempotent-Replayed"));
        (await RegistrosAsync(c)).Should().Be(3);
    }

    // ------------------------------------------------------------------ concorrência

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Duas_requisicoes_paralelas_com_a_mesma_chave_geram_um_unico_efeito(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);
        var chave = NovaChave();

        await using var trava = await op.Segurar(this, c);
        var t1 = Enviar(c, op, chave);
        var t2 = Enviar(c, op, chave);
        if (trava is not null)
            (await EsperarBloqueadosAsync(2, t1, t2)).Should().BeTrue("a 1ª espera o recurso e a 2ª espera a chave da 1ª");
        if (trava is not null)
            await trava.LiberarAsync();

        var respostas = await Task.WhenAll(t1, t2);

        respostas.Should().OnlyContain(r => r.StatusCode == op.Status);
        respostas.Count(r => r.Headers.Contains("Idempotent-Replayed")).Should().Be(1, "uma executou, a outra repetiu");
        (await op.Efeitos(this, c)).Should().Be(1);
        (await RegistrosAsync(c)).Should().Be(1);
    }

    // ------------------------------------------------------------------ rollback e falhas

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Erro_4xx_da_operacao_faz_rollback_e_a_chave_pode_ser_usada_de_novo(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);
        var chave = NovaChave();

        var falha = await Enviar(c, op, chave, falhar: true);

        ((int)falha.StatusCode).Should().BeInRange(400, 499);
        falha.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        (await RegistrosAsync(c)).Should().Be(0, "4xx não persiste a chave");
        (await op.Efeitos(this, c)).Should().Be(0);

        var certa = await Enviar(c, op, chave);

        certa.StatusCode.Should().Be(op.Status, "a chave foi liberada pelo rollback; corpo diferente não dá 422 sem registro");
        (await op.Efeitos(this, c)).Should().Be(1);
        (await RegistrosAsync(c)).Should().Be(1);
    }

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Falha_definitiva_nao_persiste_a_chave_nem_o_efeito(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);
        var chave = NovaChave();

        _commit.ArmarFalhaAntesDoCommit(transitoria: false);
        var falha = await Enviar(c, op, chave);

        falha.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        _commit.Falhas.Should().Be(1);
        (await RegistrosAsync(c)).Should().Be(0);
        (await op.Efeitos(this, c)).Should().Be(0);

        _commit.Desarmar();
        (await Enviar(c, op, chave)).StatusCode.Should().Be(op.Status);
        (await op.Efeitos(this, c)).Should().Be(1);
    }

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Falha_antes_do_commit_e_repetida_e_grava_uma_vez(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);

        _commit.ArmarFalhaAntesDoCommit();
        var resposta = await Enviar(c, op, NovaChave());

        resposta.StatusCode.Should().Be(op.Status);
        _commit.Falhas.Should().Be(1);
        (await op.Efeitos(this, c)).Should().Be(1);
        (await RegistrosAsync(c)).Should().Be(1);
    }

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Commit_ambiguo_recupera_o_resultado_confirmado_e_o_replay_continua_valendo(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);
        var chave = NovaChave();

        _commit.ArmarCommitAmbiguo();
        var primeira = await Enviar(c, op, chave);

        primeira.StatusCode.Should().Be(op.Status);
        _commit.Falhas.Should().Be(1, "o COMMIT confirmou e a resposta se perdeu");
        (await op.Efeitos(this, c)).Should().Be(1);
        (await RegistrosAsync(c)).Should().Be(1);

        var segunda = await Enviar(c, op, chave);
        segunda.StatusCode.Should().Be(op.Status);
        segunda.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
        (await segunda.Content.ReadAsStringAsync()).Should().Be(await primeira.Content.ReadAsStringAsync());
        (await op.Efeitos(this, c)).Should().Be(1);
    }

    // ------------------------------------------------------------------ efeitos que não podem duplicar

    [Fact]
    public async Task Venda_com_chave_nao_duplica_faturamento_estoque_comissao_e_auditoria()
    {
        var c = await ArranjarAsync();
        var chave = NovaChave();

        await Enviar(c, Op("venda"), chave);
        await Enviar(c, Op("venda"), chave);

        var vendas = await _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().Where(v => v.NegocioId == c.NegocioId).Select(v => new { v.Total }).ToListAsync());
        vendas.Should().ContainSingle().Which.Total.Should().Be(20m);
        (await EstoqueAsync(c)).Should().Be((99, 99, 2), "100 − 1 vendido; movimentos = estoque inicial + a venda");
        (await AuditoriaAsync(c, "VendaProduto")).Should().Be(1);
    }

    [Fact]
    public async Task Consumo_com_chave_nao_duplica_debito_nem_estoque()
    {
        var c = await ArranjarAsync();
        var chave = NovaChave();

        await Enviar(c, Op("consumo"), chave);
        await Enviar(c, Op("consumo"), chave);

        var debitos = await _fabrica.NoBancoAsync(db => db.LancamentosSaldoDevedor.IgnoreQueryFilters().Where(l => l.NegocioId == c.NegocioId).Select(l => l.Valor).ToListAsync());
        debitos.Should().ContainSingle().Which.Should().Be(5m, "1 × custo 5,00");
        (await EstoqueAsync(c)).Should().Be((99, 99, 2));
        (await AuditoriaAsync(c, "LancarConsumoInterno")).Should().Be(1);
    }

    [Fact]
    public async Task Entrada_com_chave_soma_o_estoque_uma_vez_e_devolve_o_mesmo_resumo()
    {
        var c = await ArranjarAsync();
        var chave = NovaChave();

        var primeira = await (await Enviar(c, Op("entrada"), chave)).Content.ReadFromJsonAsync<ProdutoResumo>();
        await Enviar(c, Op("entrada"), chave);
        var repetida = await (await Enviar(c, Op("entrada"), chave)).Content.ReadFromJsonAsync<ProdutoResumo>();

        primeira!.QuantidadeEstoque.Should().Be(105);
        repetida.Should().BeEquivalentTo(primeira, "o replay devolve o resumo da primeira vez");
        (await EstoqueAsync(c)).Should().Be((105, 105, 2));
        (await AuditoriaAsync(c, "EntradaEstoque")).Should().Be(1);
    }

    [Fact]
    public async Task Resgate_com_chave_consome_so_um_lote_de_selos_mesmo_com_selos_para_dois()
    {
        var c = await ArranjarAsync();
        var chave = NovaChave();

        (await Enviar(c, Op("resgate"), chave)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Enviar(c, Op("resgate"), chave)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _fabrica.NoBancoAsync(db => db.SelosCliente.IgnoreQueryFilters().CountAsync(s => s.NegocioId == c.NegocioId && s.Resgatado)))
            .Should().Be(2, "uma recompensa = 2 selos; sem a chave, o segundo clique resgataria outros 2");
    }

    [Fact]
    public async Task Resgate_sem_selos_suficientes_segue_400_e_nao_guarda_a_chave()
    {
        var c = await ArranjarAsync();
        var clienteSemSelos = Guid.NewGuid();

        var resposta = await c.Admin.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/painel/fidelidade/clientes/{clienteSemSelos}/resgatar")
        {
            Headers = { { "Idempotency-Key", NovaChave() } },
        });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RegistrosAsync(c)).Should().Be(0);
    }

    [Fact]
    public async Task Forcado_replay_nao_reenvia_o_aviso_ao_profissional()
    {
        var c = await ArranjarAsync();
        var chave = NovaChave();
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espia.Enviados.Clear();

        await Enviar(c, Op("forcado"), chave);
        await Enviar(c, Op("forcado"), chave);

        espia.Enviados.Count(e => e.Destinatario == EmailProf).Should().Be(1);
        (await AuditoriaAsync(c, "ForcarAgendamento")).Should().Be(1);
    }

    [Fact]
    public async Task Encaixe_com_cliente_rapido_sem_telefone_nao_cria_dois_clientes_com_a_mesma_chave()
    {
        var c = await ArranjarAsync();
        var antigo = await _fabrica.NoBancoAsync(async db =>
        {
            var x = Plataforma.Dominio.Clientes.Cliente.Criar(c.NegocioId, "Antigo Sem Telefone", null);
            db.Clientes.Add(x);
            await db.SaveChangesAsync();
            return x.Id;
        });
        var chave = NovaChave();
        var corpo = new LancarEncaixe(c.Agenda.ProfissionalId, null, new NovoClienteEncaixe("Cliente Novo Do Encaixe", null),
            [c.Agenda.ServicoId], Local(c, 19), false, false, null, "Encaixe especial");

        var t1 = EnviarCorpo(c, "/painel/encaixes", corpo, chave);
        var t2 = EnviarCorpo(c, "/painel/encaixes", corpo, chave);
        var respostas = await Task.WhenAll(t1, t2);
        var terceira = await EnviarCorpo(c, "/painel/encaixes", corpo, chave);

        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        terceira.StatusCode.Should().Be(HttpStatusCode.Created);
        var semTelefone = await _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters()
            .Where(x => x.NegocioId == c.NegocioId && x.Telefone == null).Select(x => new { x.Id, x.Nome }).ToListAsync());
        semTelefone.Select(x => x.Nome).Should().BeEquivalentTo(["Antigo Sem Telefone", "Cliente Novo Do Encaixe"]);
        var inicio = Local(c, 19);
        var agendamentos = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters()
            .Where(a => a.NegocioId == c.NegocioId && a.Inicio == inicio).ToListAsync());
        agendamentos.Should().ContainSingle().Which.ClienteId.Should().NotBe(antigo);
    }

    [Fact]
    public async Task Venda_com_cliente_rapido_sem_telefone_nao_cria_dois_clientes_com_a_mesma_chave()
    {
        var c = await ArranjarAsync();
        var chave = NovaChave();
        var corpo = new LancarVenda([new ItemLancarVenda(c.Produto, 1, 20m)], c.Agenda.ProfissionalId, null,
            NovoCliente: new NovoClienteVenda("Cliente Novo Da Venda", null));

        var respostas = await Task.WhenAll(EnviarCorpo(c, "/painel/vendas", corpo, chave), EnviarCorpo(c, "/painel/vendas", corpo, chave));
        await EnviarCorpo(c, "/painel/vendas", corpo, chave);

        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        (await _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().CountAsync(x => x.NegocioId == c.NegocioId && x.Nome == "Cliente Novo Da Venda")))
            .Should().Be(1);
        (await Op("venda").Efeitos(this, c)).Should().Be(1);
    }

    // ------------------------------------------------------------------ definição dos endpoints

    protected sealed record Endpoint(
        string Nome, HttpStatusCode Status, bool SemCorpo,
        Func<Cenario, int, string> Url, Func<Cenario, int, object?> Corpo, Func<Cenario, object?> Falha,
        Func<IdempotenciaOperacoesBase, Cenario, Task<int>> Efeitos,
        Func<IdempotenciaOperacoesBase, Cenario, Task<TravaExterna?>> Segurar);

    protected static Endpoint Op(string nome) => nome switch
    {
        "venda" => new(nome, HttpStatusCode.Created, false,
            (c, _) => "/painel/vendas",
            (c, v) => new LancarVenda([new ItemLancarVenda(c.Produto, v == 0 ? 1 : 2, 20m)], c.Agenda.ProfissionalId, null),
            c => new LancarVenda([new ItemLancarVenda(c.Produto, 9999, 20m)], c.Agenda.ProfissionalId, null),
            (t, c) => t._fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().CountAsync(v => v.NegocioId == c.NegocioId)),
            (t, c) => t.TravarProdutoAsync(c)),
        "entrada" => new(nome, HttpStatusCode.OK, false,
            (c, _) => $"/painel/estoque/produtos/{c.Produto}/entradas",
            (c, v) => new RegistrarEntrada(v == 0 ? 5 : 6, 3m),
            c => new RegistrarEntrada(0, 3m),
            (t, c) => t._fabrica.NoBancoAsync(db => db.MovimentosEstoque.IgnoreQueryFilters().CountAsync(m =>
                m.ProdutoId == c.Produto && m.Tipo == TipoMovimentoEstoque.Entrada && (m.Observacao == null || m.Observacao != "Estoque inicial"))),
            (t, c) => t.TravarProdutoAsync(c)),
        "consumo" => new(nome, HttpStatusCode.Created, false,
            (c, _) => $"/painel/estoque/produtos/{c.Produto}/consumos",
            (c, v) => new LancarConsumo(c.Agenda.ProfissionalId, v == 0 ? 1 : 2),
            c => new LancarConsumo(c.Agenda.ProfissionalId, 9999),
            (t, c) => t._fabrica.NoBancoAsync(db => db.LancamentosSaldoDevedor.IgnoreQueryFilters().CountAsync(l =>
                l.NegocioId == c.NegocioId && l.Tipo == TipoLancamentoSaldo.ConsumoInterno)),
            (t, c) => t.TravarProdutoAsync(c)),
        "vale" => new(nome, HttpStatusCode.Created, false,
            (c, _) => "/painel/saldos/vales",
            (c, v) => new LancarVale(c.Agenda.ProfissionalId, v == 0 ? 30m : 31m),
            c => new LancarVale(c.Agenda.ProfissionalId, -5m),
            (t, c) => t._fabrica.NoBancoAsync(db => db.LancamentosSaldoDevedor.IgnoreQueryFilters().CountAsync(l =>
                l.NegocioId == c.NegocioId && l.Tipo == TipoLancamentoSaldo.Vale)),
            (t, c) => Task.FromResult<TravaExterna?>(null)),
        "forcado" => new(nome, HttpStatusCode.Created, false,
            (c, _) => "/painel/agendamentos/forcados",
            (c, v) => new
            {
                profissionalId = c.Agenda.ProfissionalId, clienteId = c.Agenda.ClienteId, servicoIds = new[] { c.Agenda.ServicoId },
                inicio = Local(c, 19), observacoes = (string?)null, motivo = v == 0 ? "Cliente especial" : "Outro motivo",
            },
            c => new
            {
                profissionalId = c.Agenda.ProfissionalId, clienteId = c.Agenda.ClienteId, servicoIds = new[] { c.Agenda.ServicoId },
                inicio = Local(c, 19), observacoes = (string?)null, motivo = "",
            },
            (t, c) => t.AgendamentosDoTesteAsync(c),
            (t, c) => t.TravarAgendaAsync(c)),
        "encaixe" => new(nome, HttpStatusCode.Created, false,
            (c, _) => "/painel/encaixes",
            (c, v) => new LancarEncaixe(c.Agenda.ProfissionalId, c.Agenda.ClienteId, null, [c.Agenda.ServicoId],
                Local(c, v == 0 ? 19 : 20), false, false, null, "Encaixe especial"),
            c => new LancarEncaixe(c.Agenda.ProfissionalId, c.Agenda.ClienteId, null, [], Local(c, 19), false, false, null, "Encaixe especial"),
            (t, c) => t.AgendamentosDoTesteAsync(c),
            (t, c) => t.TravarAgendaAsync(c)),
        "resgate" => new(nome, HttpStatusCode.NoContent, true,
            (c, v) => $"/painel/fidelidade/clientes/{(v == 0 ? c.Agenda.ClienteId : Guid.NewGuid())}/resgatar",
            (c, v) => null,
            c => null,
            (t, c) => t._fabrica.NoBancoAsync(async db => await db.SelosCliente.IgnoreQueryFilters().CountAsync(s => s.NegocioId == c.NegocioId && s.Resgatado) / 2),
            (t, c) => Task.FromResult<TravaExterna?>(null)),
        _ => throw new ArgumentOutOfRangeException(nameof(nome)),
    };

    // ------------------------------------------------------------------ envio

    protected async Task<HttpResponseMessage> Enviar(
        Cenario c, Endpoint op, string? chave, int variante = 0, bool formatoAlternativo = false, bool falhar = false, HttpClient? cliente = null)
    {
        using var requisicao = Requisicao(c, op, chave, variante, formatoAlternativo, falhar);
        return await (cliente ?? c.Admin).SendAsync(requisicao);
    }

    protected static HttpRequestMessage Requisicao(Cenario c, Endpoint op, string? chave, int variante, bool formatoAlternativo, bool falhar = false)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, falhar && op.Nome == "resgate" ? op.Url(c, 1) : op.Url(c, variante));
        var corpo = falhar ? op.Falha(c) : op.Corpo(c, variante);
        if (corpo is not null)
            requisicao.Content = formatoAlternativo ? Alternativo(corpo) : JsonContent.Create(corpo, options: Web);
        if (chave is not null)
            requisicao.Headers.Add("Idempotency-Key", chave);
        return requisicao;
    }

    /// <summary>Mesmo conteúdo: propriedades em ordem inversa e JSON indentado.</summary>
    private static StringContent Alternativo(object corpo)
    {
        var json = (JsonObject)JsonSerializer.SerializeToNode(corpo, Web)!;
        var invertido = new JsonObject();
        foreach (var propriedade in json.ToList().AsEnumerable().Reverse())
        {
            json.Remove(propriedade.Key);
            invertido[propriedade.Key] = propriedade.Value;
        }

        return new StringContent(invertido.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, "application/json");
    }

    private async Task<HttpResponseMessage> EnviarCorpo(Cenario c, string url, object corpo, string chave)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo, options: Web) };
        requisicao.Headers.Add("Idempotency-Key", chave);
        return await c.Admin.SendAsync(requisicao);
    }

    // ------------------------------------------------------------------ apoio

    protected sealed record Cenario(Guid NegocioId, HttpClient Admin, SemeadorDeAgenda.CenarioDeAgenda Agenda, DateOnly Segunda, Guid Produto);

    protected async Task<Cenario> ArranjarAsync()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var agenda = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);

        var criado = await admin.PostAsJsonAsync("/painel/estoque/produtos", new CriarProduto("Pomada Idem", null, 5m, 20m, 100));
        criado.EnsureSuccessStatusCode();
        var produto = await criado.Content.ReadFromJsonAsync<Guid>();

        // Programa de 2 selos e 4 selos do cliente: sem a chave, dois cliques resgatariam duas recompensas.
        await _fabrica.NoBancoAsync(async db =>
        {
            db.ProgramasFidelidade.Add(ProgramaFidelidade.Criar(negocioId, 2, "Um corte"));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET email = {EmailProf} WHERE id = {agenda.ProfissionalId}");
            return await db.SaveChangesAsync();
        });
        for (var i = 0; i < 4; i++)
        {
            var agendamento = await _fabrica.SemearAgendamentoAsync(negocioId, agenda.ProfissionalId,
                SemeadorDeComissoes.Local(segunda.AddDays(1 + i), 10, 0), precos: [50m], servicoId: agenda.ServicoId);
            await _fabrica.NoBancoAsync(async db =>
            {
                db.SelosCliente.Add(SeloCliente.Criar(negocioId, agenda.ClienteId, agendamento));
                return await db.SaveChangesAsync();
            });
        }

        _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Clear();
        return new Cenario(negocioId, admin, agenda, segunda, produto);
    }

    protected static DateTimeOffset Local(Cenario c, int hora) => SemeadorDeComissoes.Local(c.Segunda, hora, 0);

    protected static string NovaChave() => Guid.NewGuid().ToString();

    /// <summary>Agendamentos criados pelos endpoints de agenda (19h/20h da segunda) — os 4 semeados para os selos ficam de fora.</summary>
    private Task<int> AgendamentosDoTesteAsync(Cenario c)
    {
        var (inicio, fim) = (Local(c, 19), Local(c, 20));
        return _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters()
            .CountAsync(a => a.NegocioId == c.NegocioId && a.Inicio >= inicio && a.Inicio <= fim));
    }

    protected Task<int> RegistrosAsync(Cenario c) =>
        _fabrica.NoBancoAsync(db => db.RequisicoesIdempotentes.IgnoreQueryFilters().CountAsync(r => r.NegocioId == c.NegocioId));

    private Task<int> AuditoriaAsync(Cenario c, string acao) =>
        _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.NegocioId == c.NegocioId && l.Acao == acao));

    protected Task<(int, int, int)> EstoqueAsync(Cenario c) =>
        _fabrica.NoBancoAsync(async db => (
            await db.Produtos.IgnoreQueryFilters().Where(p => p.Id == c.Produto).Select(p => p.QuantidadeEstoque).SingleAsync(),
            await db.MovimentosEstoque.IgnoreQueryFilters().Where(m => m.ProdutoId == c.Produto).SumAsync(m => m.Quantidade),
            await db.MovimentosEstoque.IgnoreQueryFilters().CountAsync(m => m.ProdutoId == c.Produto)));

    private async Task<TravaExterna?> TravarProdutoAsync(Cenario c) =>
        await TravaExterna.AbrirAsync(_postgres.ConnectionString, "SELECT 1 FROM produtos WHERE id = @id FOR UPDATE", c.Produto);

    private async Task<TravaExterna?> TravarAgendaAsync(Cenario c) =>
        await TravaExterna.AbrirAsync(_postgres.ConnectionString,
            "SELECT pg_advisory_xact_lock(hashtextextended(@texto, 0))", texto: c.Agenda.ProfissionalId.ToString());

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

    /// <summary>Transação externa que segura um recurso que a operação precisa depois de reivindicar a chave.</summary>
    protected sealed class TravaExterna : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conexao;
        private readonly NpgsqlTransaction _transacao;
        private bool _encerrada;

        private TravaExterna(NpgsqlConnection conexao, NpgsqlTransaction transacao)
        {
            _conexao = conexao;
            _transacao = transacao;
        }

        public static async Task<TravaExterna> AbrirAsync(string connectionString, string sql, Guid? id = null, string? texto = null)
        {
            var conexao = new NpgsqlConnection(connectionString);
            await conexao.OpenAsync();
            var transacao = await conexao.BeginTransactionAsync();
            await using var comando = new NpgsqlCommand(sql, conexao, transacao);
            if (id is not null)
                comando.Parameters.AddWithValue("id", id.Value);
            if (texto is not null)
                comando.Parameters.AddWithValue("texto", texto);
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
        }
    }
}
