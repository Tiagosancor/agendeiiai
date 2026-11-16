using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Infraestrutura.Seguranca;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Usuarios;

/// <summary>Conta quantas vezes o bcrypt foi conferido — prova a anti-enumeração por tempo sem medir relógio (nada de teste instável).</summary>
public sealed class HasherEspiao : ISenhaHasher
{
    private readonly SenhaHasher _real = new();
    private int _verificacoes;

    public int Verificacoes => Volatile.Read(ref _verificacoes);

    public string Hash(string senha) => _real.Hash(senha);

    public bool Verificar(string senha, string hash)
    {
        Interlocked.Increment(ref _verificacoes);
        return _real.Verificar(senha, hash);
    }
}

/// <summary>Guarda tudo que foi logado, para conferir que nenhum identificador, código ou senha vaza para o log.</summary>
public sealed class CapturaDeLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Linhas { get; } = new();

    public ILogger CreateLogger(string categoria) => new Registrador(Linhas);

    public void Dispose()
    {
    }

    private sealed class Registrador(ConcurrentQueue<string> linhas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            linhas.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}

/// <summary>Login por e-mail, telefone ou nome de usuário, hash fictício contra enumeração por tempo e bloqueio por identificador.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class LoginPorIdentificadorTestes : IAsyncLifetime
{
    private const string Senha = "SenhaForte!123";

    private readonly PostgresContainerFixture _postgres;
    private readonly HasherEspiao _hasher = new();
    private readonly CapturaDeLogs _logs = new();
    private PlataformaWebApplicationFactory _fabrica = null!;

    public LoginPorIdentificadorTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = Criar(null);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private PlataformaWebApplicationFactory Criar(IReadOnlyDictionary<string, string?>? configuracao) =>
        new(_postgres.ConnectionString, configuracao, servicos =>
        {
            servicos.AddSingleton<ISenhaHasher>(_hasher);
            servicos.AddSingleton<ILoggerProvider>(_logs);
        });

    private async Task<Guid> CriarUsuarioAsync(string? email, string? nomeUsuario, string? telefoneLogin, bool ativo = true)
    {
        return await _fabrica.NoBancoAsync(async db =>
        {
            var negocio = Negocio.Criar(Slug.Criar("lg-" + Guid.NewGuid().ToString("N")[..12]), "Negócio", TipoNegocio.Barbearia);
            db.Negocios.Add(negocio);
            var usuario = Usuario.Criar(
                negocio.Id, "Pessoa", email, nomeUsuario is null ? null : NomeUsuario.Criar(nomeUsuario),
                telefoneLogin is null ? null : TelefoneE164.Criar(telefoneLogin), Perfil.Recepcionista, _hasher.Hash(Senha));
            if (!ativo)
                usuario.Desativar();
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            return usuario.Id;
        });
    }

    private static Task<HttpResponseMessage> Entrar(HttpClient cliente, string identificador, string senha) =>
        cliente.PostAsJsonAsync("/painel/auth/login", new { identificador, senha });

    [Fact]
    public async Task Entra_por_email_nome_de_usuario_e_telefone_em_qualquer_forma_brasileira_com_a_mesma_senha()
    {
        await CriarUsuarioAsync("ana@teste.com", "ana.silva", "(71) 98888-7777");
        using var cliente = _fabrica.CreateClient();

        foreach (var identificador in new[]
        {
            "ana@teste.com", "  ANA@Teste.com ", "ana.silva", "Ana.Silva",
            "71988887777", "(71) 98888-7777", "+55 71 98888-7777", "5571988887777", "7188887777", "+55 71 8888-7777",
        })
        {
            var resposta = await Entrar(cliente, identificador, Senha);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, $"o identificador '{identificador.Length} caracteres' deveria entrar");
            (await resposta.Content.ReadFromJsonAsync<Dictionary<string, object>>())!.Should().ContainKey("accessToken");
        }
    }

    [Fact]
    public async Task Corpo_antigo_so_com_email_continua_funcionando()
    {
        await CriarUsuarioAsync("antigo@teste.com", null, null);
        using var cliente = _fabrica.CreateClient();

        (await cliente.PostAsJsonAsync("/painel/auth/login", new { email = "antigo@teste.com", senha = Senha }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.PostAsJsonAsync("/painel/auth/login", new { senha = Senha })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Usuario_sem_email_entra_so_pelo_nome_de_usuario_e_pelo_telefone()
    {
        await CriarUsuarioAsync(null, "so.usuario", "71 98888-1111");
        using var cliente = _fabrica.CreateClient();

        (await Entrar(cliente, "so.usuario", Senha)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Entrar(cliente, "71988881111", Senha)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Inexistente_invalido_inativo_e_senha_errada_dao_a_mesma_resposta_e_sempre_passam_pelo_bcrypt()
    {
        await CriarUsuarioAsync("existe@teste.com", "existe.mesmo", null);
        await CriarUsuarioAsync("inativo@teste.com", null, null, ativo: false);
        using var cliente = _fabrica.CreateClient();

        var casos = new[]
        {
            ("nao.existe@teste.com", Senha),     // e-mail inexistente
            ("nao.existe", Senha),               // nome de usuário inexistente
            ("71955554444", Senha),              // telefone inexistente
            ("12", Senha),                       // identificador inválido (nem telefone nem nome de usuário)
            ("jo ao!", Senha),                   // inválido
            ("existe@teste.com", "SenhaErrada!9"), // senha errada
            ("inativo@teste.com", Senha),        // inativo (senha certa, mas conta desativada)
        };

        var antes = _hasher.Verificacoes;
        var corpos = new HashSet<string>();
        foreach (var (identificador, senha) in casos)
        {
            var resposta = await Entrar(cliente, identificador, senha);
            resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            // O corpo é o mesmo em todos os casos (descontado o traceId, que muda por requisição).
            corpos.Add(System.Text.RegularExpressions.Regex.Replace(await resposta.Content.ReadAsStringAsync(), "\"traceId\":\"[^\"]*\"", ""));
        }

        corpos.Should().ContainSingle("a resposta não pode diferenciar os casos");

        // Uma conferência de bcrypt por tentativa, em todos os casos — inclusive onde não há usuário.
        (_hasher.Verificacoes - antes).Should().Be(casos.Length);
    }

    [Fact]
    public async Task Cinco_falhas_bloqueiam_o_identificador_existente_ou_nao_e_a_senha_certa_tambem_recebe_429_sem_conferir_senha()
    {
        await CriarUsuarioAsync("alvo@teste.com", "alvo.conta", null);
        using var cliente = _fabrica.CreateClient();

        foreach (var identificador in new[] { "alvo@teste.com", "fantasma@teste.com", "fantasma.usuario" })
        {
            for (var i = 0; i < 5; i++)
                (await Entrar(cliente, identificador, "Errada!123")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var antes = _hasher.Verificacoes;
            var bloqueada = await Entrar(cliente, identificador, Senha);
            bloqueada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            (await bloqueada.Content.ReadAsStringAsync()).Should().Contain("muitas_tentativas");
            _hasher.Verificacoes.Should().Be(antes, "bloqueado não confere senha");
        }

        // Outro identificador da MESMA pessoa (nome de usuário) tem o próprio contador.
        (await Entrar(cliente, "alvo.conta", Senha)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sucesso_zera_o_contador_e_as_variacoes_do_mesmo_telefone_contam_juntas()
    {
        await CriarUsuarioAsync(null, "tel.conta", "71 98888-2222");
        using var cliente = _fabrica.CreateClient();

        for (var i = 0; i < 4; i++)
            (await Entrar(cliente, "tel.conta", "Errada!123")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Entrar(cliente, "tel.conta", Senha)).StatusCode.Should().Be(HttpStatusCode.OK);
        // Zerou: mais quatro falhas ainda não bloqueiam.
        for (var i = 0; i < 4; i++)
            (await Entrar(cliente, "tel.conta", "Errada!123")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Entrar(cliente, "tel.conta", Senha)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Mesmo celular escrito de jeitos diferentes (com e sem o 9º dígito, com e sem +55) divide o contador.
        string[] formas = ["71988882222", "+55 71 8888-2222", "(71) 98888-2222", "7188882222", "5571988882222"];
        foreach (var forma in formas)
            (await Entrar(cliente, forma, "Errada!123")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Entrar(cliente, "71988882222", Senha)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Limites_vem_da_configuracao_por_variavel_de_ambiente()
    {
        await _fabrica.DisposeAsync();
        _fabrica = Criar(new Dictionary<string, string?>
        {
            ["Autenticacao:MaximoFalhasLoginPorIdentificador"] = "2",
            ["Autenticacao:LimiteLoginPorIpPorMinuto"] = "100",
        });
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
        await CriarUsuarioAsync("config@teste.com", null, null);
        using var cliente = _fabrica.CreateClient();

        (await Entrar(cliente, "config@teste.com", "Errada!123")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Entrar(cliente, "config@teste.com", "Errada!123")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Entrar(cliente, "config@teste.com", Senha)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Limite_por_ip_responde_429_acima_do_configurado()
    {
        await _fabrica.DisposeAsync();
        _fabrica = Criar(new Dictionary<string, string?> { ["Autenticacao:LimiteLoginPorIpPorMinuto"] = "3" });
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
        await CriarUsuarioAsync("ip@teste.com", null, null);
        using var cliente = _fabrica.CreateClient();

        for (var i = 0; i < 3; i++)
            (await Entrar(cliente, "ip@teste.com", Senha)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Entrar(cliente, "ip@teste.com", Senha)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Contador_de_tentativas_nao_guarda_o_identificador_em_claro_e_o_log_nao_vaza_dado_pessoal()
    {
        await CriarUsuarioAsync("segredo.pessoal@teste.com", "nome.secreto", "71 98888-3333");
        using var cliente = _fabrica.CreateClient();

        for (var i = 0; i < 6; i++)
            await Entrar(cliente, i % 2 == 0 ? "segredo.pessoal@teste.com" : "nome.secreto", "Errada!123");
        for (var i = 0; i < 6; i++)
            await Entrar(cliente, "71988883333", "Errada!123");
        await Entrar(cliente, "nome.secreto", Senha); // bloqueado

        var chaves = await _fabrica.NoBancoAsync(db => db.TentativasLogin.AsNoTracking().Select(t => t.Chave).ToListAsync());
        chaves.Should().NotBeEmpty();
        chaves.Should().OnlyContain(c => c.Length == 64 && c.All(Uri.IsHexDigit), "a chave é um HMAC-SHA256 em hexadecimal");

        var tudo = string.Join("\n", _logs.Linhas);
        foreach (var proibido in new[] { "segredo.pessoal", "nome.secreto", "988883333", "98888-3333", "8888-3333", "Errada!123", Senha })
            tudo.Should().NotContain(proibido);
    }

    [Fact]
    public async Task Job_de_limpeza_apaga_so_os_contadores_sem_atividade_ha_mais_de_um_dia()
    {
        await CriarUsuarioAsync("limpa@teste.com", null, null);
        using var cliente = _fabrica.CreateClient();
        await Entrar(cliente, "limpa@teste.com", "Errada!123");

        await _fabrica.NoBancoAsync(async db =>
        {
            db.TentativasLogin.Add(new TentativaLogin("antiga", DateTimeOffset.UtcNow.AddDays(-3)));
            await db.SaveChangesAsync();
            return 0;
        });

        var removidas = await _fabrica.NoBancoAsync(db => new Plataforma.Infraestrutura.Autenticacao.JobLimparTentativasLogin(
            db, Microsoft.Extensions.Logging.Abstractions.NullLogger<Plataforma.Infraestrutura.Autenticacao.JobLimparTentativasLogin>.Instance).ExecutarAsync());

        removidas.Should().Be(1);
        (await _fabrica.NoBancoAsync(db => db.TentativasLogin.CountAsync())).Should().Be(1);
    }
}
