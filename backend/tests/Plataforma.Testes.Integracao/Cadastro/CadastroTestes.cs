using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Cadastro;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Api.Controllers.Cadastro;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Cadastro;

/// <summary>Cadastro de negócio novo (seção 6.5) e testes obrigatórios da seção 8.6 (ajuste 4c).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class CadastroTestes : IAsyncLifetime
{
    private const string SenhaValida = "SenhaForte123";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;
    private HttpClient _cliente = null!;

    public CadastroTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        _cliente = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _fabrica.DisposeAsync();
    }

    private static string EmailNovo() => $"dono-{Guid.NewGuid():N}@teste.com";

    private static string SlugNovo() => "cad-" + Guid.NewGuid().ToString("N")[..12];

    private static string TelefoneNovo() => $"+55719{Random.Shared.Next(10000000, 99999999)}";

    private async Task<Guid> PlanoAsync(string nome = "Começo")
    {
        var planos = await _cliente.GetFromJsonAsync<List<PlanoPublico>>("/cadastro/planos");
        return planos!.Single(p => p.Nome == nome).Id;
    }

    private async Task<string> ConfirmarEmailAsync(string email)
    {
        (await _cliente.PostAsJsonAsync("/cadastro/codigos", new CadastroController.SolicitarCodigoRequisicao(email, null)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var corpo = _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Last(e => e.Destinatario == email).CorpoHtml;
        var codigo = Regex.Match(corpo, @"<h2>(\d{6})</h2>").Groups[1].Value;

        var resposta = await _cliente.PostAsJsonAsync("/cadastro/codigos/validar", new CadastroController.ValidarCodigoRequisicao(email, codigo));
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tokenCadastro").GetString()!;
    }

    private async Task<HttpResponseMessage> CadastrarAsync(
        string token, string email, string slug, string telefone, string? chave = null, string senha = SenhaValida, Guid? planoId = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, "/cadastro")
        {
            Content = JsonContent.Create(new CadastroController.CadastrarRequisicao(
                token, planoId ?? await PlanoAsync(), "Mensal", "Barbearia do Teste", "Barbearia", slug,
                "Dono do Teste", email, telefone, senha, AceiteTermos: true, TokenCaptcha: null)),
        };
        requisicao.Headers.Add("Idempotency-Key", chave ?? Guid.NewGuid().ToString());
        return await _cliente.SendAsync(requisicao);
    }

    private async Task<T> NoBancoAsync<T>(Func<PlataformaDbContext, Task<T>> consulta)
    {
        using var escopo = _fabrica.Services.CreateScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>());
    }

    [Fact]
    public async Task Cadastro_completo_cria_negocio_administrador_e_teste_e_ja_permite_entrar()
    {
        var email = EmailNovo();
        var slug = SlugNovo();
        var token = await ConfirmarEmailAsync(email);

        var resposta = await CadastrarAsync(token, email, slug, TelefoneNovo(), planoId: await PlanoAsync("Ritmo"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var negocioId = (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("negocioId").GetGuid();

        var usuario = await NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().Include(u => u.Permissoes).SingleAsync(u => u.Email == email));
        usuario.NegocioId.Should().Be(negocioId);
        usuario.Perfil.Should().Be(Perfil.Administrador);
        usuario.Permissoes.Should().HaveCount(Enum.GetValues<Permissao>().Length);

        var assinatura = await NoBancoAsync(db => db.Assinaturas.IgnoreQueryFilters().SingleAsync(a => a.NegocioId == negocioId));
        assinatura.Estado.Should().Be(EstadoAssinatura.EmTeste);
        assinatura.PrecoMensalTravado.Should().Be(79.90m);
        assinatura.FimTeste.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));

        (await _cliente.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SenhaValida))).StatusCode.Should().Be(HttpStatusCode.OK);

        _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados
            .Should().Contain(e => e.Destinatario == email && e.Assunto.StartsWith("Bem-vindo") && e.CorpoHtml.Contains(slug));
    }

    [Fact]
    public async Task Email_ja_cadastrado_recebe_na_tela_a_mesma_resposta_de_um_email_novo()
    {
        var existente = EmailNovo();
        await CadastrarAsync(await ConfirmarEmailAsync(existente), existente, SlugNovo(), TelefoneNovo());

        var paraExistente = await _cliente.PostAsJsonAsync("/cadastro/codigos", new CadastroController.SolicitarCodigoRequisicao(existente, null));
        var novo = EmailNovo();
        var paraNovo = await _cliente.PostAsJsonAsync("/cadastro/codigos", new CadastroController.SolicitarCodigoRequisicao(novo, null));

        paraExistente.StatusCode.Should().Be(paraNovo.StatusCode);
        (await paraExistente.Content.ReadAsStringAsync()).Should().Be(await paraNovo.Content.ReadAsStringAsync());

        var enviados = _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados;
        var avisoExistente = enviados.Last(e => e.Destinatario == existente);
        avisoExistente.Assunto.Should().Contain("já tem uma conta");
        avisoExistente.CorpoHtml.Should().NotMatchRegex(@"\d{6}");
        enviados.Last(e => e.Destinatario == novo).CorpoHtml.Should().MatchRegex(@"<h2>\d{6}</h2>");
    }

    [Fact]
    public async Task Clique_duplo_com_a_mesma_idempotency_key_cria_um_negocio_so()
    {
        var email = EmailNovo();
        var slug = SlugNovo();
        var telefone = TelefoneNovo();
        var token = await ConfirmarEmailAsync(email);
        var chave = Guid.NewGuid().ToString();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CadastrarAsync(token, email, slug, telefone, chave)));

        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var ids = await Task.WhenAll(respostas.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("negocioId").GetGuid()));
        ids.Distinct().Should().ContainSingle();
        (await NoBancoAsync(db => db.Negocios.CountAsync(n => n.Id == ids[0]))).Should().Be(1);
        (await NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().CountAsync(u => u.Email == email))).Should().Be(1);
    }

    [Fact]
    public async Task Mesma_chave_reaproveitada_por_outro_email_nao_devolve_o_negocio_do_primeiro()
    {
        var chave = Guid.NewGuid().ToString();
        var primeiro = EmailNovo();
        await CadastrarAsync(await ConfirmarEmailAsync(primeiro), primeiro, SlugNovo(), TelefoneNovo(), chave);

        var segundo = EmailNovo();
        var resposta = await CadastrarAsync(await ConfirmarEmailAsync(segundo), segundo, SlugNovo(), TelefoneNovo(), chave);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resposta.Content.ReadAsStringAsync()).Should().NotContain("negocioId");
    }

    [Fact]
    public async Task Segundo_teste_com_o_mesmo_telefone_e_recusado()
    {
        var telefone = TelefoneNovo();
        var primeiro = EmailNovo();
        (await CadastrarAsync(await ConfirmarEmailAsync(primeiro), primeiro, SlugNovo(), telefone)).StatusCode.Should().Be(HttpStatusCode.Created);

        var segundo = EmailNovo();
        var resposta = await CadastrarAsync(await ConfirmarEmailAsync(segundo), segundo, SlugNovo(), telefone);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain(nameof(ErroCadastro.TesteJaUtilizado));
    }

    [Fact]
    public async Task Segundo_teste_com_o_mesmo_email_e_recusado_mesmo_se_o_login_trocou_de_email()
    {
        var email = EmailNovo();
        await CadastrarAsync(await ConfirmarEmailAsync(email), email, SlugNovo(), TelefoneNovo());

        // O dono trocou o e-mail de login depois; o registro do teste continua com o original.
        await NoBancoAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE usuarios SET email = {"outro-" + email} WHERE email = {email}"));

        var resposta = await CadastrarAsync(await ConfirmarEmailAsync(email), email, SlugNovo(), TelefoneNovo());

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain(nameof(ErroCadastro.TesteJaUtilizado));
    }

    [Theory]
    [InlineData("admin", HttpStatusCode.BadRequest)]
    [InlineData("cadastro", HttpStatusCode.BadRequest)]
    [InlineData("-invalido", HttpStatusCode.BadRequest)]
    public async Task Slug_reservado_ou_invalido_e_recusado_no_servidor(string slug, HttpStatusCode esperado)
    {
        var email = EmailNovo();

        var resposta = await CadastrarAsync(await ConfirmarEmailAsync(email), email, slug, TelefoneNovo());

        resposta.StatusCode.Should().Be(esperado);
    }

    [Fact]
    public async Task Slug_repetido_e_recusado_no_servidor_e_nada_fica_gravado()
    {
        var slug = SlugNovo();
        var primeiro = EmailNovo();
        await CadastrarAsync(await ConfirmarEmailAsync(primeiro), primeiro, slug, TelefoneNovo());

        var segundo = EmailNovo();
        var chave = Guid.NewGuid().ToString();
        var resposta = await CadastrarAsync(await ConfirmarEmailAsync(segundo), segundo, slug, TelefoneNovo(), chave);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == segundo))).Should().BeFalse();
        (await NoBancoAsync(db => db.ChavesIdempotencia.AnyAsync(c => c.Chave == chave))).Should().BeFalse();
    }

    [Fact]
    public async Task Falha_no_meio_da_criacao_nao_deixa_negocio_sem_administrador_nem_assinatura()
    {
        var email = EmailNovo();
        var slug = SlugNovo();
        var token = await ConfirmarEmailAsync(email);

        await using var conexao = new NpgsqlConnection(_postgres.ConnectionString);
        await conexao.OpenAsync();
        await using (var criar = new NpgsqlCommand("""
            CREATE OR REPLACE FUNCTION falhar_insert_assinatura() RETURNS trigger AS $$
            BEGIN RAISE EXCEPTION 'falha simulada no meio do cadastro'; END; $$ LANGUAGE plpgsql;
            CREATE TRIGGER falhar_assinatura BEFORE INSERT ON assinaturas FOR EACH ROW EXECUTE FUNCTION falhar_insert_assinatura();
            """, conexao))
            await criar.ExecuteNonQueryAsync();

        try
        {
            var resposta = await CadastrarAsync(token, email, slug, TelefoneNovo());

            resposta.IsSuccessStatusCode.Should().BeFalse();
        }
        finally
        {
            await using var remover = new NpgsqlCommand(
                "DROP TRIGGER falhar_assinatura ON assinaturas; DROP FUNCTION falhar_insert_assinatura();", conexao);
            await remover.ExecuteNonQueryAsync();
        }

        (await NoBancoAsync(db => db.Negocios.AnyAsync(n => n.Slug == Plataforma.Dominio.Negocios.Slug.Criar(slug)))).Should().BeFalse();
        (await NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == email))).Should().BeFalse();
        (await NoBancoAsync(db => db.RegistrosTesteGratis.AnyAsync(r => r.Email == email))).Should().BeFalse();

        // E o mesmo cadastro funciona de novo depois (nada ficou pela metade bloqueando).
        (await CadastrarAsync(token, email, slug, TelefoneNovo())).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Token_de_outro_email_e_recusado()
    {
        var token = await ConfirmarEmailAsync(EmailNovo());

        var resposta = await CadastrarAsync(token, EmailNovo(), SlugNovo(), TelefoneNovo());

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Senha_fraca_e_recusada()
    {
        var email = EmailNovo();

        var resposta = await CadastrarAsync(await ConfirmarEmailAsync(email), email, SlugNovo(), TelefoneNovo(), senha: "abcdefgh");

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sem_idempotency_key_e_recusado()
    {
        var email = EmailNovo();
        var token = await ConfirmarEmailAsync(email);

        var resposta = await _cliente.PostAsJsonAsync("/cadastro", new CadastroController.CadastrarRequisicao(
            token, await PlanoAsync(), "Mensal", "Negócio", "Barbearia", SlugNovo(), "Dono", email, TelefoneNovo(), SenhaValida, true, null));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Verificar_slug_diz_se_esta_livre_em_uso_ou_reservado()
    {
        var slug = SlugNovo();
        var email = EmailNovo();

        (await _cliente.GetFromJsonAsync<DisponibilidadeSlug>($"/cadastro/slugs/{slug}"))!.Disponivel.Should().BeTrue();
        await CadastrarAsync(await ConfirmarEmailAsync(email), email, slug, TelefoneNovo());

        (await _cliente.GetFromJsonAsync<DisponibilidadeSlug>($"/cadastro/slugs/{slug}"))!.Disponivel.Should().BeFalse();
        (await _cliente.GetFromJsonAsync<DisponibilidadeSlug>("/cadastro/slugs/admin"))!.Motivo.Should().Contain("reservado");
    }

    [Fact]
    public async Task Com_captcha_configurado_envio_de_codigo_sem_token_e_recusado()
    {
        using var cliente = _fabrica.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Captcha:ChaveSecreta"] = "segredo-de-teste" }))).CreateClient();

        var resposta = await cliente.PostAsJsonAsync("/cadastro/codigos", new CadastroController.SolicitarCodigoRequisicao(EmailNovo(), null));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Checklist_aparece_no_primeiro_acesso_e_some_quando_tudo_foi_feito()
    {
        var email = EmailNovo();
        await CadastrarAsync(await ConfirmarEmailAsync(email), email, SlugNovo(), TelefoneNovo());
        var login = await (await _cliente.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SenhaValida))).Content.ReadFromJsonAsync<RespostaLogin>();
        using var painel = _fabrica.CreateClient();
        painel.DefaultRequestHeaders.Authorization = new("Bearer", login!.AccessToken);

        var inicial = await painel.GetFromJsonAsync<PrimeirosPassos>("/painel/primeiros-passos");
        inicial!.Exibir.Should().BeTrue();
        inicial.ServicosCadastrados.Should().BeFalse();

        var categoria = await (await painel.PostAsJsonAsync("/painel/categorias", new { nome = "Cabelo" })).Content.ReadFromJsonAsync<Guid>();
        await painel.PostAsJsonAsync("/painel/servicos", new { categoriaId = categoria, nome = "Corte", preco = 40, duracaoMinutos = 30, popular = false });
        var profissional = await (await painel.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Zé"))).Content.ReadFromJsonAsync<Guid>();
        await painel.PutAsJsonAsync($"/painel/profissionais/{profissional}/horarios",
            new[] { new { diaSemana = 1, inicio = "09:00", fim = "18:00" } });
        await painel.PostAsync("/painel/primeiros-passos/link-copiado", null);

        var final = await painel.GetFromJsonAsync<PrimeirosPassos>("/painel/primeiros-passos");
        final!.Should().BeEquivalentTo(new { ServicosCadastrados = true, ProfissionaisCadastrados = true, HorariosConfigurados = true, LinkCopiado = true, Exibir = false });
    }
}
