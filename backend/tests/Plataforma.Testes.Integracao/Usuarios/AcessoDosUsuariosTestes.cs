using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using System.Text.Json.Serialization;
using Xunit;

namespace Plataforma.Testes.Integracao.Usuarios;

/// <summary>
/// Criação e edição do acesso dos usuários pelo painel com e-mail opcional, nome de usuário e telefone de login: unicidade global,
/// erros com <c>codigo</c> e sem o valor, "administrador define nova senha", convite que verifica o e-mail e esqueci-senha só para
/// e-mail verificado.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AcessoDosUsuariosTestes : IAsyncLifetime
{
    private const string Senha = "SenhaForte!123";

    private static readonly JsonSerializerOptions JsonApi = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public AcessoDosUsuariosTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Autenticacao:LimiteConflitosIdentificadorPorHora"] = "6",
        });
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private static async Task<JsonElement> CorpoAsync(HttpResponseMessage resposta) =>
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string Codigo(JsonElement corpo) => corpo.GetProperty("codigo").GetString()!;

    private static async Task<Guid> IdCriadoAsync(HttpResponseMessage resposta)
    {
        resposta.StatusCode.Should().Be(HttpStatusCode.Created, await resposta.Content.ReadAsStringAsync());
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    private static Task<HttpResponseMessage> Criar(HttpClient admin, CriarUsuario dados) => admin.PostAsJsonAsync("/painel/usuarios", dados);

    private async Task<HttpStatusCode> LoginAsync(string identificador, string senha)
    {
        using var cliente = _fabrica.CreateClient();
        return (await cliente.PostAsJsonAsync("/painel/auth/login", new { identificador, senha })).StatusCode;
    }

    [Fact]
    public async Task Cria_usuario_sem_email_so_com_nome_de_usuario_e_telefone_de_login_e_ele_entra_por_eles()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var id = await IdCriadoAsync(await Criar(admin, new CriarUsuario(
            "Sem E-mail", null, Senha, Perfil.Recepcionista, Telefone: "71 3333-4444", NomeUsuario: "sem.email", TelefoneLogin: "(71) 8888-5555")));

        var detalhe = (await admin.GetFromJsonAsync<UsuarioDetalhe>($"/painel/usuarios/{id}", JsonApi))!;
        detalhe.Email.Should().BeNull();
        detalhe.EmailVerificado.Should().BeFalse();
        detalhe.NomeUsuario.Should().Be("sem.email");
        detalhe.TelefoneLogin.Should().Be("+5571988885555", "o telefone de login fica na forma canônica");

        (await LoginAsync("sem.email", Senha)).Should().Be(HttpStatusCode.OK);
        (await LoginAsync("71988885555", Senha)).Should().Be(HttpStatusCode.OK);
        (await LoginAsync("7188885555", Senha)).Should().Be(HttpStatusCode.OK);

        // O telefone de CONTATO nunca vira login sozinho.
        (await LoginAsync("7133334444", Senha)).Should().Be(HttpStatusCode.Unauthorized);

        var lista = (await admin.GetFromJsonAsync<List<UsuarioResumo>>("/painel/usuarios", JsonApi))!;
        lista.Single(u => u.Id == id).Email.Should().BeNull();
    }

    [Fact]
    public async Task Email_digitado_pelo_administrador_nasce_nao_verificado_e_senha_nasce_definida_por_terceiro()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var id = await IdCriadoAsync(await Criar(admin, new CriarUsuario("Com E-mail", "Novo@Teste.com", Senha, Perfil.Recepcionista)));

        (await admin.GetFromJsonAsync<UsuarioDetalhe>($"/painel/usuarios/{id}", JsonApi))!
            .Should().Match<UsuarioDetalhe>(u => u.Email == "novo@teste.com" && !u.EmailVerificado);
        var banco = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == id));
        banco.SenhaDefinidaPorTerceiro.Should().BeTrue();
    }

    [Fact]
    public async Task Erros_de_validacao_trazem_codigo()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        async Task Esperar(CriarUsuario dados, string codigo)
        {
            var resposta = await Criar(admin, dados);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            Codigo(await CorpoAsync(resposta)).Should().Be(codigo);
        }

        await Esperar(new CriarUsuario("X", null, Senha, Perfil.Recepcionista), "identificador_obrigatorio");
        await Esperar(new CriarUsuario("X", "   ", Senha, Perfil.Recepcionista), "identificador_obrigatorio");
        await Esperar(new CriarUsuario("X", "sem-arroba", Senha, Perfil.Recepcionista), "email_invalido");
        await Esperar(new CriarUsuario("X", null, Senha, Perfil.Recepcionista, NomeUsuario: "12345678"), "nome_usuario_invalido");
        await Esperar(new CriarUsuario("X", null, Senha, Perfil.Recepcionista, NomeUsuario: "Admin"), "nome_usuario_reservado");
        await Esperar(new CriarUsuario("X", null, Senha, Perfil.Recepcionista, NomeUsuario: "Plataforma de Testes"), "nome_usuario_invalido");
        await Esperar(new CriarUsuario("X", null, Senha, Perfil.Recepcionista, NomeUsuario: "plataformadetestes"), "nome_usuario_reservado");
        await Esperar(new CriarUsuario("X", null, Senha, Perfil.Recepcionista, TelefoneLogin: "123"), "telefone_invalido");
        await Esperar(new CriarUsuario("X", "x@teste.com", "curta", Perfil.Recepcionista), "senha_fraca");
    }

    [Fact]
    public async Task Identificador_repetido_em_outro_negocio_da_409_com_campo_e_sem_o_valor_inclusive_o_celular_com_e_sem_o_nono_digito()
    {
        var (admin1, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (admin2, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await IdCriadoAsync(await Criar(admin1, new CriarUsuario(
            "Primeiro", "primeiro@teste.com", Senha, Perfil.Recepcionista, NomeUsuario: "dupla.conta", TelefoneLogin: "71 98888-6666")));

        async Task<(string Campo, string? Sugestao, string Texto)> Conflito(CriarUsuario dados)
        {
            var resposta = await Criar(admin2, dados);
            resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var texto = await resposta.Content.ReadAsStringAsync();
            var corpo = JsonDocument.Parse(texto).RootElement;
            Codigo(corpo).Should().Be("identificador_indisponivel");
            return (corpo.GetProperty("campo").GetString()!, corpo.TryGetProperty("sugestao", out var s) ? s.GetString() : null, texto);
        }

        var email = await Conflito(new CriarUsuario("Segundo", "PRIMEIRO@teste.com", Senha, Perfil.Recepcionista));
        email.Campo.Should().Be("email");
        email.Texto.Should().NotContain("primeiro@teste.com");

        var nome = await Conflito(new CriarUsuario("Segundo", null, Senha, Perfil.Recepcionista, NomeUsuario: "Dupla.Conta"));
        nome.Campo.Should().Be("nomeUsuario");
        nome.Sugestao.Should().Be("dupla.conta2");

        // Mesmo celular, sem o 9º dígito e com +55: é a mesma conta.
        (await Conflito(new CriarUsuario("Segundo", null, Senha, Perfil.Recepcionista, TelefoneLogin: "7188886666"))).Campo.Should().Be("telefoneLogin");
        var telefone = await Conflito(new CriarUsuario("Segundo", null, Senha, Perfil.Recepcionista, TelefoneLogin: "+55 71 98888-6666"));
        telefone.Campo.Should().Be("telefoneLogin");
        telefone.Texto.Should().NotContain("98888").And.NotContain("8888-6666");
    }

    [Fact]
    public async Task Limite_de_conflitos_por_autor_vira_429()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await IdCriadoAsync(await Criar(admin, new CriarUsuario("Base", null, Senha, Perfil.Recepcionista, NomeUsuario: "ocupado.nome")));

        for (var i = 0; i < 6; i++)
            (await Criar(admin, new CriarUsuario("Outro", null, Senha, Perfil.Recepcionista, NomeUsuario: "ocupado.nome")))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var bloqueada = await Criar(admin, new CriarUsuario("Outro", null, Senha, Perfil.Recepcionista, NomeUsuario: "livre.nome"));
        bloqueada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        Codigo(await CorpoAsync(bloqueada)).Should().Be("muitas_tentativas");
    }

    [Fact]
    public async Task Corrida_de_duas_criacoes_com_o_mesmo_nome_de_usuario_e_o_mesmo_telefone_uma_passa_e_a_outra_recebe_409()
    {
        var (admin1, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (admin2, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var porNome = await Task.WhenAll(
            Criar(admin1, new CriarUsuario("A", null, Senha, Perfil.Recepcionista, NomeUsuario: "corrida.nome")),
            Criar(admin2, new CriarUsuario("B", null, Senha, Perfil.Recepcionista, NomeUsuario: "corrida.nome")));
        porNome.Select(r => r.StatusCode).Order().Should().Equal(HttpStatusCode.Created, HttpStatusCode.Conflict);

        var porTelefone = await Task.WhenAll(
            Criar(admin1, new CriarUsuario("A", null, Senha, Perfil.Recepcionista, TelefoneLogin: "71 98888-7000")),
            Criar(admin2, new CriarUsuario("B", null, Senha, Perfil.Recepcionista, TelefoneLogin: "71 8888-7000")));
        porTelefone.Select(r => r.StatusCode).Order().Should().Equal(HttpStatusCode.Created, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Excluir_o_usuario_libera_os_tres_identificadores()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await IdCriadoAsync(await Criar(admin, new CriarUsuario(
            "Efêmero", "efemero@teste.com", Senha, Perfil.Recepcionista, NomeUsuario: "efemero", TelefoneLogin: "71 98888-8000")));

        (await admin.DeleteAsync($"/painel/usuarios/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        await IdCriadoAsync(await Criar(admin, new CriarUsuario(
            "Reocupou", "efemero@teste.com", Senha, Perfil.Recepcionista, NomeUsuario: "efemero", TelefoneLogin: "71 8888-8000")));
    }

    [Fact]
    public async Task Excluir_logicamente_solta_os_identificadores_e_apaga_o_email_pendente()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await IdCriadoAsync(await Criar(admin, new CriarUsuario("Com Histórico", null, Senha, Perfil.Recepcionista, NomeUsuario: "com.historico")));
        await _fabrica.NoBancoAsync(async db =>
        {
            var u = await db.Usuarios.IgnoreQueryFilters().FirstAsync(x => x.Id == id);
            db.LogsAuditoriaNegocio.Add(new Plataforma.Dominio.Auditoria.LogAuditoriaNegocio(u.NegocioId, id, "x", "Editar", "Usuario", id, null));
            db.VerificacoesEmailUsuario.Add(VerificacaoEmailUsuario.Criar(u.NegocioId, id, "pendente@teste.com", "hash", DateTimeOffset.UtcNow.AddMinutes(5)));
            await db.SaveChangesAsync();
            return 0;
        });

        (await admin.DeleteAsync($"/painel/usuarios/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var banco = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == id));
        (banco.Excluido, banco.Email, banco.NomeUsuario, banco.TelefoneLogin).Should().Be((true, null, null, null));
        (await _fabrica.NoBancoAsync(db => db.VerificacoesEmailUsuario.IgnoreQueryFilters().CountAsync(v => v.UsuarioId == id))).Should().Be(0);
    }

    [Fact]
    public async Task Put_acesso_troca_os_tres_e_nunca_deixa_a_conta_sem_nenhum()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await IdCriadoAsync(await Criar(admin, new CriarUsuario("Troca", "troca@teste.com", Senha, Perfil.Recepcionista)));

        // Só e-mail -> só nome de usuário (primeiro entra o novo, depois sai o antigo).
        (await admin.PutAsJsonAsync($"/painel/usuarios/{id}/acesso", new DefinirAcessoUsuario(null, "troca.user", null)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoginAsync("troca@teste.com", Senha)).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync("troca.user", Senha)).Should().Be(HttpStatusCode.OK);

        var vazio = await admin.PutAsJsonAsync($"/painel/usuarios/{id}/acesso", new DefinirAcessoUsuario(null, null, null));
        vazio.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Codigo(await CorpoAsync(vazio)).Should().Be("identificador_obrigatorio");

        // E-mail novo volta a não verificado; telefone novo entra.
        (await admin.PutAsJsonAsync($"/painel/usuarios/{id}/acesso", new DefinirAcessoUsuario("Novo@Teste.com", "troca.user", "71 98888-9000")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detalhe = (await admin.GetFromJsonAsync<UsuarioDetalhe>($"/painel/usuarios/{id}", JsonApi))!;
        (detalhe.Email, detalhe.EmailVerificado, detalhe.TelefoneLogin).Should().Be(("novo@teste.com", false, "+5571988889000"));

        (await admin.PutAsJsonAsync($"/painel/usuarios/{Guid.NewGuid()}/acesso", new DefinirAcessoUsuario("a@b.com", null, null)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var log = await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.EntidadeId == id && l.Acao == "DefinirIdentificadoresAcesso").Select(l => l.Detalhes).ToListAsync());
        log.Should().NotBeEmpty().And.OnlyContain(d => d!.StartsWith("Campos:") && !d.Contains("@") && !d.Contains("988889000"));
    }

    [Fact]
    public async Task Put_acesso_exige_a_permissao_de_editar_cadastros()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (gerente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Recepcionista, Permissao.GerenciarUsuarios);
        _ = admin;

        (await gerente.PutAsJsonAsync($"/painel/usuarios/{Guid.NewGuid()}/acesso", new DefinirAcessoUsuario("a@b.com", null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        negocioId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Administrador_define_nova_senha_forca_minima_revoga_sessoes_marca_terceiro_audita_e_a_propria_conta_da_409()
    {
        var (admin, _, adminId, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await IdCriadoAsync(await Criar(admin, new CriarUsuario("Alvo", null, Senha, Perfil.Recepcionista, NomeUsuario: "alvo.senha")));

        using var cookies = _fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await cookies.PostAsJsonAsync("/painel/auth/login", new { identificador = "alvo.senha", senha = Senha });
        var cookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        var fraca = await admin.PutAsJsonAsync($"/painel/usuarios/{id}/senha", new AlterarSenhaRequisicao("curta"));
        fraca.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Codigo(await CorpoAsync(fraca)).Should().Be("senha_fraca");

        var propria = await admin.PutAsJsonAsync($"/painel/usuarios/{adminId}/senha", new AlterarSenhaRequisicao("OutraSenha123"));
        propria.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Codigo(await CorpoAsync(propria)).Should().Be("operacao_bloqueada");

        (await admin.PutAsJsonAsync($"/painel/usuarios/{id}/senha", new AlterarSenhaRequisicao("NovaSenha2026"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await LoginAsync("alvo.senha", Senha)).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync("alvo.senha", "NovaSenha2026")).Should().Be(HttpStatusCode.OK);

        var renovar = new HttpRequestMessage(HttpMethod.Post, "/painel/auth/renovar");
        renovar.Headers.Add("Cookie", cookie);
        (await cookies.SendAsync(renovar)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "as sessões antigas foram revogadas");

        (await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == id)))
            .SenhaDefinidaPorTerceiro.Should().BeTrue();
        var log = await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.EntidadeId == id && l.Acao == "RedefinirSenhaUsuario").Select(l => l.Detalhes).ToListAsync());
        log.Should().ContainSingle().Which.Should().BeNull("a senha nunca vai para a auditoria");
    }

    [Fact]
    public async Task Convite_sem_email_e_400_e_com_email_nao_verificado_o_link_usado_verifica_o_email()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        var semEmail = await IdCriadoAsync(await Criar(admin, new CriarUsuario("Sem", null, Senha, Perfil.Recepcionista, NomeUsuario: "sem.convite")));
        var comEmail = await IdCriadoAsync(await Criar(admin, new CriarUsuario("Com", "convidada@teste.com", Senha, Perfil.Recepcionista)));

        var recusado = await admin.PostAsync($"/painel/usuarios/{semEmail}/convite", null);
        recusado.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Codigo(await CorpoAsync(recusado)).Should().Be("convite_sem_email");

        (await admin.PostAsync($"/painel/usuarios/{comEmail}/convite", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == comEmail))).EmailVerificado.Should().BeFalse();

        var mensagem = espia.Enviados.Last(e => e.Destinatario == "convidada@teste.com");
        var token = Regex.Match(mensagem.CorpoHtml, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        token.Should().NotBeEmpty();

        using var anonimo = _fabrica.CreateClient();
        (await anonimo.PostAsJsonAsync("/painel/auth/redefinir-senha", new RequisicaoRedefinirSenha(token, "SenhaDaConvidada1")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var usuario = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == comEmail));
        usuario.EmailVerificado.Should().BeTrue("usar o link do convite prova a posse do e-mail");
        usuario.SenhaDefinidaPorTerceiro.Should().BeFalse();
    }

    [Fact]
    public async Task Convite_perde_a_validade_se_o_email_for_trocado_antes_de_usar()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        var id = await IdCriadoAsync(await Criar(admin, new CriarUsuario("Troca", "primeiro.endereco@teste.com", Senha, Perfil.Recepcionista)));
        await admin.PostAsync($"/painel/usuarios/{id}/convite", null);
        var token = Regex.Match(espia.Enviados.Last(e => e.Destinatario == "primeiro.endereco@teste.com").CorpoHtml, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;

        (await admin.PutAsJsonAsync($"/painel/usuarios/{id}/acesso", new DefinirAcessoUsuario("segundo.endereco@teste.com", null, null)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var anonimo = _fabrica.CreateClient();
        (await anonimo.PostAsJsonAsync("/painel/auth/redefinir-senha", new RequisicaoRedefinirSenha(token, "SenhaDaConvidada1")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == id))).EmailVerificado.Should().BeFalse();
    }

    [Fact]
    public async Task Esqueci_senha_so_envia_para_email_verificado_e_responde_202_igual()
    {
        var (admin, _, _, emailVerificado) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        await IdCriadoAsync(await Criar(admin, new CriarUsuario("Nao Verificado", "naoverificado@teste.com", Senha, Perfil.Recepcionista)));

        using var anonimo = _fabrica.CreateClient();
        var antes = espia.Enviados.Count;
        (await anonimo.PostAsJsonAsync("/painel/auth/esqueci-senha", new RequisicaoEsqueciSenha("naoverificado@teste.com"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        espia.Enviados.Count.Should().Be(antes, "e-mail não verificado não recebe redefinição");

        (await anonimo.PostAsJsonAsync("/painel/auth/esqueci-senha", new RequisicaoEsqueciSenha(emailVerificado))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        espia.Enviados.Count.Should().Be(antes + 1);
    }

    [Fact]
    public async Task Profissional_com_acesso_so_por_nome_de_usuario_e_convite_exige_email()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var criado = await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional(
            "Zz Profissional", Telefone: "71 3333-1111",
            Acesso: new AcessoProfissional(null, Senha, false, NomeUsuario: "zz.profissional")));
        criado.StatusCode.Should().Be(HttpStatusCode.Created, await criado.Content.ReadAsStringAsync());
        var profissionalId = await criado.Content.ReadFromJsonAsync<Guid>();
        (await LoginAsync("zz.profissional", Senha)).Should().Be(HttpStatusCode.OK);

        var detalhe = (await admin.GetFromJsonAsync<ProfissionalDetalhe>($"/painel/profissionais/{profissionalId}", JsonApi))!;
        (detalhe.Acesso!.Email, detalhe.Acesso.NomeUsuario, detalhe.Acesso.TelefoneLogin, detalhe.Acesso.EmailVerificado)
            .Should().Be((null, "zz.profissional", null, false));
        (await LoginAsync("7133331111", Senha)).Should().Be(HttpStatusCode.Unauthorized, "o telefone de contato do profissional não vira login");

        var semEmail = await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional(
            "Zz Outro", Acesso: new AcessoProfissional(null, null, true, NomeUsuario: "zz.outro")));
        semEmail.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Codigo(await CorpoAsync(semEmail)).Should().Be("convite_sem_email");

        var repetido = await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional(
            "Zz Terceiro", Acesso: new AcessoProfissional(null, Senha, false, NomeUsuario: "ZZ.Profissional")));
        repetido.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CorpoAsync(repetido)).GetProperty("campo").GetString().Should().Be("nomeUsuario");
    }

    [Fact]
    public async Task Tamanhos_acima_do_limite_viram_400_com_codigo_e_nunca_500_em_todos_os_caminhos()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var emailLongo = new string('a', 311) + "@teste.com"; // 321 caracteres: acima da coluna
        var nomeLongo = new string('n', 31);
        var telefoneLongo = new string('7', 200);
        var senhaGigante = new string('A', 200) + "1";

        async Task Esperar400(Task<HttpResponseMessage> chamada, string codigo)
        {
            var resposta = await chamada;
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            Codigo(await CorpoAsync(resposta)).Should().Be(codigo);
        }

        await Esperar400(Criar(admin, new CriarUsuario("X", emailLongo, Senha, Perfil.Recepcionista)), "email_invalido");
        await Esperar400(Criar(admin, new CriarUsuario("X", null, Senha, Perfil.Recepcionista, NomeUsuario: nomeLongo)), "nome_usuario_invalido");
        await Esperar400(Criar(admin, new CriarUsuario("X", null, Senha, Perfil.Recepcionista, NomeUsuario: new string('n', 5000))), "nome_usuario_invalido");
        await Esperar400(Criar(admin, new CriarUsuario("X", null, Senha, Perfil.Recepcionista, TelefoneLogin: telefoneLongo)), "telefone_invalido");
        await Esperar400(Criar(admin, new CriarUsuario("X", "x@teste.com", senhaGigante, Perfil.Recepcionista)), "senha_fraca");

        var id = await IdCriadoAsync(await Criar(admin, new CriarUsuario("Y", "y@teste.com", Senha, Perfil.Recepcionista)));
        await Esperar400(admin.PutAsJsonAsync($"/painel/usuarios/{id}/acesso", new DefinirAcessoUsuario(emailLongo, null, null)), "email_invalido");
        await Esperar400(admin.PutAsJsonAsync($"/painel/usuarios/{id}", new AtualizarUsuario("Y", null, null, Email: emailLongo)), "email_invalido");
        await Esperar400(admin.PutAsJsonAsync($"/painel/usuarios/{id}/senha", new AlterarSenhaRequisicao(senhaGigante)), "senha_fraca");

        // Profissional: acesso com e-mail longo.
        await Esperar400(admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional(
            "Zz Longo", Acesso: new AcessoProfissional(emailLongo, Senha, false))), "email_invalido");

        // Login e esqueci-senha com entradas gigantes: nunca 500.
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.PostAsJsonAsync("/painel/auth/login", new { identificador = new string('x', 400), senha = Senha })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.PostAsJsonAsync("/painel/auth/login", new { identificador = "y@teste.com", senha = new string('s', 500) })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.PostAsJsonAsync("/painel/auth/esqueci-senha", new { email = emailLongo })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await anonimo.PostAsJsonAsync("/painel/auth/esqueci-senha", new { email = new string('z', 5000) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bearer_de_quem_teve_a_senha_definida_pelo_administrador_deixa_de_valer_na_hora()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await IdCriadoAsync(await Criar(admin, new CriarUsuario("Alvo", null, Senha, Perfil.Recepcionista, NomeUsuario: "alvo.bearer")));

        using var anonimo = _fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new { identificador = "alvo.bearer", senha = Senha });
        var alvo = _fabrica.CreateClient();
        alvo.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<RespostaLogin>())!.AccessToken);
        (await alvo.GetAsync("/painel/minha-conta")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PutAsJsonAsync($"/painel/usuarios/{id}/senha", new AlterarSenhaRequisicao("NovaSenha2026"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await alvo.GetAsync("/painel/minha-conta")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.GetAsync("/painel/usuarios")).StatusCode.Should().Be(HttpStatusCode.OK, "a sessão de quem trocou a senha de outro não é afetada");
    }
}
