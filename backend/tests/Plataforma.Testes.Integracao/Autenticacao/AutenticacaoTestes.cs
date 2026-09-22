using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Autenticacao;

/// <summary>Sprint 1 (seção 4): "JWT de curta duração + refresh token em cookie httpOnly".</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AutenticacaoTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public AutenticacaoTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Login_com_credenciais_corretas_devolve_tokens_e_seta_cookie_httponly()
    {
        var (_, _, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        using var cliente = _fabrica.CreateClient();
        var resposta = await cliente.PostAsJsonAsync(
            "/painel/auth/login", new RequisicaoLogin(email, SemeadorDeUsuarios.SenhaPadrao));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        var corpo = await resposta.Content.ReadFromJsonAsync<RespostaLogin>();
        corpo!.AccessToken.Should().NotBeNullOrWhiteSpace();

        resposta.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookie = cookies!.Single(c => c.StartsWith("refresh_token="));
        cookie.Should().ContainEquivalentOf("httponly");
        cookie.Should().Contain("path=/painel/auth");
    }

    [Fact]
    public async Task Login_com_senha_errada_da_401()
    {
        var (_, _, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        using var cliente = _fabrica.CreateClient();
        var resposta = await cliente.PostAsJsonAsync(
            "/painel/auth/login", new RequisicaoLogin(email, "senha-errada"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_com_email_inexistente_da_401_igual_a_senha_errada()
    {
        using var cliente = _fabrica.CreateClient();
        var resposta = await cliente.PostAsJsonAsync(
            "/painel/auth/login", new RequisicaoLogin("nao-existe@teste.com", "qualquer-senha"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_de_usuario_desativado_da_401()
    {
        var (_, negocioId, usuarioId, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        using (var escopo = _fabrica.Services.CreateScope())
        {
            var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

            // Escopo novo, sem negócio resolvido (não passou pelo pipeline HTTP/JWT) — o
            // filtro multi-tenant bloquearia um FindAsync normal (comportamento correto e
            // esperado, seção 8.3.1), então ignora o filtro de propósito aqui.
            var usuario = await dbContext.Usuarios.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == usuarioId && u.NegocioId == negocioId);

            usuario!.Desativar();
            await dbContext.SaveChangesAsync();
        }

        using var cliente = _fabrica.CreateClient();
        var resposta = await cliente.PostAsJsonAsync(
            "/painel/auth/login", new RequisicaoLogin(email, SemeadorDeUsuarios.SenhaPadrao));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Renovar_sem_cookie_de_refresh_da_401()
    {
        using var cliente = _fabrica.CreateClient();
        var resposta = await cliente.PostAsync("/painel/auth/renovar", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Fluxo_completo_login_renovar_logout()
    {
        var (_, _, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        // HandleCookies=false: controla o cookie manualmente para garantir que o teste usa
        // exatamente o valor que ele mesmo extraiu, sem o jar automático do HttpClient
        // reescrevendo o cabeçalho Cookie com o valor mais recente por baixo dos panos.
        using var cliente = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var respostaLogin = await cliente.PostAsJsonAsync(
            "/painel/auth/login", new RequisicaoLogin(email, SemeadorDeUsuarios.SenhaPadrao));
        respostaLogin.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookieRefresh = ExtrairCookieRefresh(respostaLogin);

        var respostaRenovar = await EnviarComCookieAsync(cliente, "/painel/auth/renovar", cookieRefresh);
        respostaRenovar.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookieRenovado = ExtrairCookieRefresh(respostaRenovar);

        // A rotação troca o refresh token a cada uso — a prova mais confiável disso é o
        // valor do cookie mudar (o JWT de acesso pode até repetir se cair no mesmo segundo,
        // já que "iat"/"exp" têm granularidade de segundo).
        cookieRenovado.Should().NotBe(cookieRefresh);

        var respostaLogout = await EnviarComCookieAsync(cliente, "/painel/auth/logout", cookieRenovado);
        respostaLogout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Depois do logout, o refresh token não vale mais.
        var respostaRenovarDepoisDoLogout = await EnviarComCookieAsync(cliente, "/painel/auth/renovar", cookieRenovado);
        respostaRenovarDepoisDoLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Renovar_com_refresh_token_ja_usado_falha_por_causa_da_rotacao()
    {
        var (_, _, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        using var cliente = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var respostaLogin = await cliente.PostAsJsonAsync(
            "/painel/auth/login", new RequisicaoLogin(email, SemeadorDeUsuarios.SenhaPadrao));
        var cookieOriginal = ExtrairCookieRefresh(respostaLogin);

        (await EnviarComCookieAsync(cliente, "/painel/auth/renovar", cookieOriginal))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Reenvia o cookie ORIGINAL (já revogado pela rotação — seção 4).
        var respostaComTokenVelho = await EnviarComCookieAsync(cliente, "/painel/auth/renovar", cookieOriginal);

        respostaComTokenVelho.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Extrai só "nome=valor" do Set-Cookie da resposta, pronto para virar cabeçalho Cookie na próxima requisição.</summary>
    private static string ExtrairCookieRefresh(HttpResponseMessage resposta)
    {
        resposta.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookieCompleto = cookies!.Single(c => c.StartsWith("refresh_token="));
        return cookieCompleto[..cookieCompleto.IndexOf(';')];
    }

    private static Task<HttpResponseMessage> EnviarComCookieAsync(HttpClient cliente, string caminho, string cookie)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, caminho);
        requisicao.Headers.Add("Cookie", cookie);
        return cliente.SendAsync(requisicao);
    }
}
