using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Assinaturas;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using System.Text.Json.Serialization;
using Xunit;

namespace Plataforma.Testes.Integracao.Usuarios;

/// <summary>"Minha conta": completar e confirmar o e-mail, nome de usuário e troca de senha do próprio usuário logado.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class MinhaContaTestes : IAsyncLifetime
{
    private const string Senha = SemeadorDeUsuarios.SenhaPadrao;

    private static readonly JsonSerializerOptions JsonApi = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly PostgresContainerFixture _postgres;
    private readonly CapturaDeLogs _logs = new();
    private PlataformaWebApplicationFactory _fabrica = null!;

    public MinhaContaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, null,
            servicos => servicos.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(_logs));
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    /// <summary>Usuário sem e-mail (só nome de usuário), logado.</summary>
    private async Task<(HttpClient Cliente, Guid UsuarioId, Guid NegocioId, string NomeUsuario)> SemEmailAsync()
    {
        var nome = "u" + Guid.NewGuid().ToString("N")[..10];
        var (negocioId, usuarioId) = await _fabrica.NoBancoAsync(async db =>
        {
            var negocio = Negocio.Criar(Slug.Criar("mc-" + Guid.NewGuid().ToString("N")[..12]), "Negócio", TipoNegocio.Barbearia);
            db.Negocios.Add(negocio);
            var hasher = _fabrica.Services.GetRequiredService<ISenhaHasher>();
            var usuario = Usuario.Criar(negocio.Id, "Sem Email", null, NomeUsuario.Criar(nome), null, Perfil.Administrador, hasher.Hash(Senha), senhaDefinidaPorTerceiro: true);
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            return (negocio.Id, usuario.Id);
        });

        using var anonimo = _fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new { identificador = nome, senha = Senha });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<RespostaLogin>())!.AccessToken;
        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (cliente, usuarioId, negocioId, nome);
    }

    private static async Task<string> CodigoDoCodigoAsync(EspiaEmail espia, string destino)
    {
        await Task.Yield();
        var mensagem = espia.Enviados.Last(e => e.Destinatario == destino);
        return Regex.Match(mensagem.CorpoHtml, @"<h2>(\d{6})</h2>").Groups[1].Value;
    }

    private static string Codigo(JsonElement corpo) => corpo.GetProperty("codigo").GetString()!;

    [Fact]
    public async Task Obter_mostra_a_conta_do_proprio_token_sem_id_na_rota()
    {
        var (cliente, _, _, nome) = await SemEmailAsync();

        var conta = (await cliente.GetFromJsonAsync<MinhaConta>("/painel/minha-conta", JsonApi))!;

        (conta.Nome, conta.Perfil, conta.Email, conta.EmailVerificado, conta.NomeUsuario, conta.SenhaDefinidaPorTerceiro)
            .Should().Be(("Sem Email", Perfil.Administrador, null, false, nome, true));
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.GetAsync("/painel/minha-conta")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Fluxo_completo_senha_errada_codigo_errado_esgotado_e_depois_codigo_certo_verifica_e_a_claim_email_aparece_na_renovacao()
    {
        var (cliente, usuarioId, _, nome) = await SemEmailAsync();
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();

        var senhaErrada = await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "dono@teste.com", senhaAtual = "Errada!123" });
        senhaErrada.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Codigo(JsonDocument.Parse(await senhaErrada.Content.ReadAsStringAsync()).RootElement).Should().Be("senha_incorreta");

        (await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "sem-arroba", senhaAtual = Senha }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "Dono@Teste.com", senhaAtual = Senha }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var certo = await CodigoDoCodigoAsync(espia, "dono@teste.com");
        certo.Should().HaveLength(6);

        var pendente = (await cliente.GetFromJsonAsync<MinhaConta>("/painel/minha-conta", JsonApi))!;
        pendente.EmailPendenteMascarado.Should().Be("d***@teste.com");
        pendente.EmailVerificado.Should().BeFalse();

        // Três erros esgotam o código, e depois nem o certo vale.
        var errado = certo == "000000" ? "111111" : "000000";
        for (var i = 0; i < 3; i++)
        {
            var resposta = await cliente.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = errado });
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        var esgotado = await cliente.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = certo });
        esgotado.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Codigo(JsonDocument.Parse(await esgotado.Content.ReadAsStringAsync()).RootElement).Should().Be("codigo_invalido");

        // Pede de novo: um código novo, e esse vale.
        (await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "dono@teste.com", senhaAtual = Senha })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var novo = await CodigoDoCodigoAsync(espia, "dono@teste.com");
        (await cliente.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = novo })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var conta = (await cliente.GetFromJsonAsync<MinhaConta>("/painel/minha-conta", JsonApi))!;
        (conta.Email, conta.EmailVerificado, conta.EmailPendenteMascarado).Should().Be(("dono@teste.com", true, null));

        // Uso único.
        (await cliente.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = novo })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Depois da confirmação o e-mail entra nas claims (token novo) e no rótulo de auditoria.
        using var anonimo = _fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new { identificador = nome, senha = Senha });
        var token = (await login.Content.ReadFromJsonAsync<RespostaLogin>())!.AccessToken;
        new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Should().Contain(c => c.Type == "email" && c.Value == "dono@teste.com");

        // A confirmação grava a auditoria só com o nome do campo.
        var log = await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.EntidadeId == usuarioId && l.Acao == "ConfirmarEmailUsuario").Select(l => l.Detalhes).ToListAsync());
        log.Should().ContainSingle().Which.Should().Be("Campos: e-mail");
    }

    [Fact]
    public async Task Email_de_outra_conta_responde_202_igual_sem_enviar_codigo_avisa_o_dono_e_a_confirmacao_falha_igual_a_codigo_errado()
    {
        var (cliente, _, _, _) = await SemEmailAsync();
        var (_, _, _, emailDeOutro) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        var antes = espia.Enviados.Count;

        var resposta = await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = emailDeOutro, senhaAtual = Senha });

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await resposta.Content.ReadAsStringAsync()).Should().BeEmpty();
        var novos = espia.Enviados.Skip(antes).ToList();
        novos.Should().ContainSingle().Which.Destinatario.Should().Be(emailDeOutro);
        novos[0].CorpoHtml.Should().NotMatchRegex(@"\d{6}", "nenhum código vai para o endereço de outra conta");
        novos[0].Assunto.Should().Contain("já tem acesso");

        (await cliente.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = "123456" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await cliente.GetFromJsonAsync<MinhaConta>("/painel/minha-conta", JsonApi))!.EmailVerificado.Should().BeFalse();
    }

    [Fact]
    public async Task Limite_de_pedidos_de_codigo_responde_202_igual_e_nao_envia()
    {
        var (cliente, _, _, _) = await SemEmailAsync();
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        var antes = espia.Enviados.Count;

        for (var i = 0; i < 5; i++)
            (await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "limite@teste.com", senhaAtual = Senha }))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);

        espia.Enviados.Count.Should().Be(antes + 3, "3 por hora por usuário (e por destino); o resto responde igual e não envia");
    }

    [Fact]
    public async Task Duas_contas_confirmando_o_mesmo_email_so_uma_fica_com_ele()
    {
        var (a, idA, _, _) = await SemEmailAsync();
        var (b, idB, _, _) = await SemEmailAsync();
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();

        await a.PostAsJsonAsync("/painel/minha-conta/email", new { email = "disputa@teste.com", senhaAtual = Senha });
        var codigoA = await CodigoDoCodigoAsync(espia, "disputa@teste.com");
        await b.PostAsJsonAsync("/painel/minha-conta/email", new { email = "disputa@teste.com", senhaAtual = Senha });
        var codigoB = await CodigoDoCodigoAsync(espia, "disputa@teste.com");

        var respostas = await Task.WhenAll(
            a.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = codigoA }),
            b.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = codigoB }));

        respostas.Select(r => r.StatusCode).Order().Should().Equal(HttpStatusCode.NoContent, HttpStatusCode.BadRequest);
        (await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().CountAsync(u => u.Email == "disputa@teste.com" && (u.Id == idA || u.Id == idB))))
            .Should().Be(1);
    }

    [Fact]
    public async Task Trocar_o_email_avisa_o_endereco_verificado_anterior()
    {
        var (cliente, _, _, emailAtual) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();

        await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "novo.endereco@teste.com", senhaAtual = Senha });
        var codigo = await CodigoDoCodigoAsync(espia, "novo.endereco@teste.com");
        (await cliente.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var aviso = espia.Enviados.Last(e => e.Destinatario == emailAtual);
        aviso.Assunto.Should().Contain("alterado");
        aviso.CorpoHtml.Should().Contain("n***@teste.com").And.NotContain("novo.endereco");
    }

    [Fact]
    public async Task Nome_de_usuario_exige_a_senha_atual_e_respeita_unicidade_e_a_regra_de_ficar_com_algum_identificador()
    {
        var (cliente, _, _, nome) = await SemEmailAsync();
        var (outro, _, _, nomeDoOutro) = await SemEmailAsync();
        _ = outro;

        (await cliente.PutAsJsonAsync("/painel/minha-conta/nome-usuario", new { nomeUsuario = "novo.nome.x", senhaAtual = "Errada!123" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var conflito = await cliente.PutAsJsonAsync("/painel/minha-conta/nome-usuario", new { nomeUsuario = nomeDoOutro, senhaAtual = Senha });
        conflito.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonDocument.Parse(await conflito.Content.ReadAsStringAsync()).RootElement.GetProperty("campo").GetString().Should().Be("nomeUsuario");

        (await cliente.PutAsJsonAsync("/painel/minha-conta/nome-usuario", new { nomeUsuario = "admin", senhaAtual = Senha }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await cliente.PutAsJsonAsync("/painel/minha-conta/nome-usuario", new { nomeUsuario = "novo.nome.x", senhaAtual = Senha }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.PostAsJsonAsync("/painel/auth/login", new { identificador = "novo.nome.x", senha = Senha })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonimo.PostAsJsonAsync("/painel/auth/login", new { identificador = nome, senha = Senha })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Era o único identificador: não dá para remover.
        var remover = await cliente.PutAsJsonAsync("/painel/minha-conta/nome-usuario", new { nomeUsuario = (string?)null, senhaAtual = Senha });
        remover.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Codigo(JsonDocument.Parse(await remover.Content.ReadAsStringAsync()).RootElement).Should().Be("identificador_obrigatorio");
    }

    [Fact]
    public async Task Trocar_a_senha_confere_a_atual_exige_forca_revoga_todas_as_sessoes_e_zera_o_aviso()
    {
        var (cliente, usuarioId, _, nome) = await SemEmailAsync();
        using var cookies = _fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await cookies.PostAsJsonAsync("/painel/auth/login", new { identificador = nome, senha = Senha });
        var cookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        (await cliente.PutAsJsonAsync("/painel/minha-conta/senha", new { senhaAtual = "Errada!123", novaSenha = "NovaSenha2026" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var fraca = await cliente.PutAsJsonAsync("/painel/minha-conta/senha", new { senhaAtual = Senha, novaSenha = "curta" });
        fraca.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Codigo(JsonDocument.Parse(await fraca.Content.ReadAsStringAsync()).RootElement).Should().Be("senha_fraca");

        (await cliente.PutAsJsonAsync("/painel/minha-conta/senha", new { senhaAtual = Senha, novaSenha = "NovaSenha2026" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var renovar = new HttpRequestMessage(HttpMethod.Post, "/painel/auth/renovar");
        renovar.Headers.Add("Cookie", cookie);
        (await cookies.SendAsync(renovar)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await cookies.PostAsJsonAsync("/painel/auth/login", new { identificador = nome, senha = "NovaSenha2026" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == usuarioId)))
            .SenhaDefinidaPorTerceiro.Should().BeFalse();
    }

    [Fact]
    public async Task Cinco_senhas_atuais_erradas_bloqueiam_a_conta_por_um_tempo_mesmo_com_a_senha_certa()
    {
        var (cliente, _, _, _) = await SemEmailAsync();

        for (var i = 0; i < 5; i++)
            (await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "x@teste.com", senhaAtual = "Errada!123" }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var bloqueada = await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "x@teste.com", senhaAtual = Senha });
        bloqueada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        Codigo(JsonDocument.Parse(await bloqueada.Content.ReadAsStringAsync()).RootElement).Should().Be("muitas_tentativas");
    }

    private async Task SuspenderAssinaturaAsync(Guid negocioId)
    {
        await _fabrica.NoBancoAsync(async db =>
        {
            var plano = await db.Planos.FirstAsync(p => p.Nome == "Casa Cheia");
            var assinatura = Plataforma.Dominio.Assinaturas.ServicoAssinatura.IniciarTeste(
                negocioId, plano, Plataforma.Dominio.Assinaturas.Periodicidade.Mensal, "teste", DateTimeOffset.UtcNow.AddDays(-45));
            Plataforma.Dominio.Assinaturas.ServicoAssinatura.AtualizarPorTempo(assinatura, DateTimeOffset.UtcNow);
            db.Assinaturas.Add(assinatura);
            await db.SaveChangesAsync();
            return 0;
        });
    }

    [Fact]
    public async Task Minha_conta_funciona_com_a_assinatura_efetivamente_suspensa_enquanto_o_resto_do_painel_responde_402()
    {
        var (cliente, _, negocioId, nome) = await SemEmailAsync();
        await SuspenderAssinaturaAsync(negocioId);
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();

        (await cliente.GetAsync("/painel/profissionais")).StatusCode.Should().Be(HttpStatusCode.PaymentRequired); // controle: está suspensa

        (await cliente.GetAsync("/painel/minha-conta")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.PutAsJsonAsync("/painel/minha-conta/nome-usuario", new { nomeUsuario = nome + "x", senhaAtual = Senha })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "suspensa@teste.com", senhaAtual = Senha })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var codigo = await CodigoDoCodigoAsync(espia, "suspensa@teste.com");
        (await cliente.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.PutAsJsonAsync("/painel/minha-conta/senha", new { senhaAtual = Senha, novaSenha = "NovaSenha2026" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Bearer_emitido_antes_da_troca_de_senha_recebe_401_e_o_novo_funciona()
    {
        var (cliente, _, _, nome) = await SemEmailAsync();
        (await cliente.GetAsync("/painel/minha-conta")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await cliente.PutAsJsonAsync("/painel/minha-conta/senha", new { senhaAtual = Senha, novaSenha = "NovaSenha2026" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await cliente.GetAsync("/painel/minha-conta")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o access token anterior à troca não vale mais");

        using var anonimo = _fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new { identificador = nome, senha = "NovaSenha2026" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var novo = _fabrica.CreateClient();
        novo.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<RespostaLogin>())!.AccessToken);
        (await novo.GetAsync("/painel/minha-conta")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_token_que_sobrevive_a_troca_de_senha_por_corrida_nao_renova()
    {
        var (_, usuarioId, _, nome) = await SemEmailAsync();
        using var cookies = _fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await cookies.PostAsJsonAsync("/painel/auth/login", new { identificador = nome, senha = Senha });
        var cookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        // A troca de senha (por qualquer caminho) sobe a versão das sessões na mesma gravação da senha.
        await _fabrica.NoBancoAsync(async db =>
        {
            var u = await db.Usuarios.IgnoreQueryFilters().FirstAsync(x => x.Id == usuarioId);
            u.AlterarSenha(_fabrica.Services.GetRequiredService<ISenhaHasher>().Hash("NovaSenha2026"));
            await db.SaveChangesAsync();
            return 0;
        });

        // Simula a corrida: uma renovação concorrente deixou um refresh token NÃO revogado depois da troca.
        (await _fabrica.NoBancoAsync(db => db.TokensAtualizacao.IgnoreQueryFilters().Where(t => t.UsuarioId == usuarioId).CountAsync())).Should().BeGreaterThan(0);
        await _fabrica.NoBancoAsync(db => db.TokensAtualizacao.IgnoreQueryFilters().Where(t => t.UsuarioId == usuarioId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevogadoEm, (DateTimeOffset?)null)));

        var renovar = new HttpRequestMessage(HttpMethod.Post, "/painel/auth/renovar");
        renovar.Headers.Add("Cookie", cookie);
        (await cookies.SendAsync(renovar)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Pedidos_simultaneos_de_codigo_nao_dao_500_e_deixam_uma_unica_verificacao_pendente()
    {
        var (cliente, usuarioId, _, _) = await SemEmailAsync();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "simultaneo@teste.com", senhaAtual = Senha })));

        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Accepted);
        var pendentes = await _fabrica.NoBancoAsync(db => db.VerificacoesEmailUsuario.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(v => v.UsuarioId == usuarioId && v.UsadoEm == null && v.InvalidadoEm == null && v.Email != null));
        pendentes.Should().Be(1, "pedido novo invalida os anteriores, mesmo simultâneo");
    }

    [Fact]
    public async Task Pedido_de_codigo_novo_e_confirmacao_simultaneos_nao_dao_500_e_terminam_consistentes()
    {
        var (cliente, usuarioId, _, _) = await SemEmailAsync();
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "corrida.confirma@teste.com", senhaAtual = Senha });
        var codigo = await CodigoDoCodigoAsync(espia, "corrida.confirma@teste.com");

        var confirmar = cliente.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo });
        var reenviar = cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "corrida.confirma@teste.com", senhaAtual = Senha });
        var respostas = await Task.WhenAll(confirmar, reenviar);

        respostas[0].StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.BadRequest);
        respostas[1].StatusCode.Should().Be(HttpStatusCode.Accepted);
        var usuario = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == usuarioId));
        if (respostas[0].StatusCode == HttpStatusCode.NoContent)
            usuario.EmailVerificado.Should().BeTrue();
        (await _fabrica.NoBancoAsync(db => db.VerificacoesEmailUsuario.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(v => v.UsuarioId == usuarioId && v.UsadoEm == null && v.InvalidadoEm == null && v.Email != null))).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Codigo_vencido_e_recusado_e_o_job_apaga_o_email_pendente_vencido_e_esgotado_e_preserva_o_valido()
    {
        var (vencida, idVencida, _, _) = await SemEmailAsync();
        var (esgotada, idEsgotada, _, _) = await SemEmailAsync();
        var (valida, idValida, _, _) = await SemEmailAsync();
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();

        await vencida.PostAsJsonAsync("/painel/minha-conta/email", new { email = "vencido@teste.com", senhaAtual = Senha });
        var codigoVencido = await CodigoDoCodigoAsync(espia, "vencido@teste.com");
        await esgotada.PostAsJsonAsync("/painel/minha-conta/email", new { email = "esgotado@teste.com", senhaAtual = Senha });
        await valida.PostAsJsonAsync("/painel/minha-conta/email", new { email = "valido@teste.com", senhaAtual = Senha });
        var codigoValido = await CodigoDoCodigoAsync(espia, "valido@teste.com");

        // Vence o código da primeira conta (relógio controlado direto no banco).
        await _fabrica.NoBancoAsync(db => db.VerificacoesEmailUsuario.IgnoreQueryFilters().Where(v => v.UsuarioId == idVencida)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ExpiraEm, DateTimeOffset.UtcNow.AddMinutes(-1))));
        (await vencida.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = codigoVencido })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Esgota a segunda: o e-mail pendente some na hora, sem esperar o job.
        for (var i = 0; i < 3; i++)
            await esgotada.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = "000000" });
        (await _fabrica.NoBancoAsync(db => db.VerificacoesEmailUsuario.IgnoreQueryFilters().AsNoTracking().Where(v => v.UsuarioId == idEsgotada).Select(v => v.Email).SingleAsync()))
            .Should().BeNull();

        var (anulados, _) = await _fabrica.NoBancoAsync(db => new Plataforma.Infraestrutura.Autenticacao.JobLimparVerificacoesEmail(
            db, Microsoft.Extensions.Logging.Abstractions.NullLogger<Plataforma.Infraestrutura.Autenticacao.JobLimparVerificacoesEmail>.Instance).ExecutarAsync());

        anulados.Should().Be(1, "só a vencida (a esgotada já estava limpa)");
        async Task<string?> EmailPendente(Guid id) => await _fabrica.NoBancoAsync(db => db.VerificacoesEmailUsuario.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.UsuarioId == id).Select(v => v.Email).SingleAsync());
        (await EmailPendente(idVencida)).Should().BeNull();
        (await EmailPendente(idValida)).Should().Be("valido@teste.com");

        // O código ainda válido continua funcionando depois do job.
        (await valida.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = codigoValido })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Limite_por_destino_vale_entre_contas_distintas_e_acima_dele_nada_e_enviado()
    {
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        var contas = new List<HttpClient>();
        for (var i = 0; i < 5; i++)
            contas.Add((await SemEmailAsync()).Cliente);
        var antes = espia.Enviados.Count;

        foreach (var conta in contas)
            (await conta.PostAsJsonAsync("/painel/minha-conta/email", new { email = "alvo.compartilhado@teste.com", senhaAtual = Senha }))
                .StatusCode.Should().Be(HttpStatusCode.Accepted, "a resposta é sempre igual");

        espia.Enviados.Skip(antes).Count(e => e.Destinatario == "alvo.compartilhado@teste.com").Should().Be(3, "3 por hora por e-mail de destino, de qualquer conta");
    }

    [Fact]
    public async Task Email_acima_de_320_caracteres_e_400_email_invalido_em_minha_conta_e_nunca_500()
    {
        var (cliente, _, _, _) = await SemEmailAsync();
        var demais = new string('a', 311) + "@teste.com"; // 321 caracteres

        var resposta = await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = demais, senhaAtual = Senha });
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Codigo(JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement).Should().Be("email_invalido");

        // Muito acima do limite do campo: recusado na borda (validação do DTO), nunca 500.
        (await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = new string('b', 5000) + "@teste.com", senhaAtual = Senha }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Logs_de_minha_conta_e_de_conflito_de_identificador_nao_vazam_dado_pessoal_nem_codigo()
    {
        var (cliente, _, _, nome) = await SemEmailAsync();
        var (outro, _, _, nomeDoOutro) = await SemEmailAsync();
        _ = outro;
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();

        await cliente.PostAsJsonAsync("/painel/minha-conta/email", new { email = "log.sigiloso@teste.com", senhaAtual = Senha });
        var codigo = await CodigoDoCodigoAsync(espia, "log.sigiloso@teste.com");
        await cliente.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo = codigo == "123456" ? "654321" : "123456" });
        await cliente.PostAsJsonAsync("/painel/minha-conta/email/confirmar", new { codigo });
        await cliente.PutAsJsonAsync("/painel/minha-conta/nome-usuario", new { nomeUsuario = nomeDoOutro, senhaAtual = Senha }); // conflito 409
        await cliente.PutAsJsonAsync("/painel/minha-conta/senha", new { senhaAtual = "Errada!123", novaSenha = "NovaSenha2026" });

        var tudo = string.Join("\n", _logs.Linhas);
        foreach (var proibido in new[] { "log.sigiloso", nome, nomeDoOutro, codigo, Senha, "NovaSenha2026", "Errada!123" })
            tudo.Should().NotContain(proibido);
    }
}
