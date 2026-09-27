using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Autenticacao;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Autenticacao;

/// <summary>"Esqueci minha senha" do painel: link por e-mail, uso único, validade curta.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class RedefinicaoSenhaTestes : IAsyncLifetime
{
    private const string SenhaNova = "NovaSenha2026";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public RedefinicaoSenhaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Fluxo_completo_troca_a_senha_e_derruba_as_sessoes_abertas()
    {
        var (_, _, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        using var cliente = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await cliente.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SemeadorDeUsuarios.SenhaPadrao));
        var cookieSessao = CookieRefresh(login);

        var token = await SolicitarEPegarTokenAsync(cliente, email);
        (await Redefinir(cliente, token, SenhaNova)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await cliente.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SenhaNova)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SemeadorDeUsuarios.SenhaPadrao)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var renovar = new HttpRequestMessage(HttpMethod.Post, "/painel/auth/renovar");
        renovar.Headers.Add("Cookie", cookieSessao);
        (await cliente.SendAsync(renovar)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Email_sem_conta_responde_igual_e_nao_envia_nada()
    {
        using var cliente = _fabrica.CreateClient();
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        var antes = espia.Enviados.Count;

        var resposta = await cliente.PostAsJsonAsync("/painel/auth/esqueci-senha", new RequisicaoEsqueciSenha("ninguem@teste.com"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        espia.Enviados.Count.Should().Be(antes);
    }

    [Fact]
    public async Task Link_so_vale_uma_vez()
    {
        var (_, _, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Recepcionista);
        using var cliente = _fabrica.CreateClient();
        var token = await SolicitarEPegarTokenAsync(cliente, email);

        (await Redefinir(cliente, token, SenhaNova)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Redefinir(cliente, token, "OutraSenha99")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await cliente.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SenhaNova)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Senha_fraca_e_recusada_sem_gastar_o_link()
    {
        var (_, _, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        using var cliente = _fabrica.CreateClient();
        var token = await SolicitarEPegarTokenAsync(cliente, email);

        var fraca = await Redefinir(cliente, token, "12345678");
        fraca.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await fraca.Content.ReadAsStringAsync()).Should().Contain("letras e números");

        (await Redefinir(cliente, token, SenhaNova)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Pedido_novo_invalida_o_link_anterior()
    {
        var (_, _, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        using var cliente = _fabrica.CreateClient();
        var primeiro = await SolicitarEPegarTokenAsync(cliente, email);
        var segundo = await SolicitarEPegarTokenAsync(cliente, email);

        (await Redefinir(cliente, primeiro, SenhaNova)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Redefinir(cliente, segundo, SenhaNova)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Link_vencido_e_recusado()
    {
        var (_, _, usuarioId, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        using var cliente = _fabrica.CreateClient();
        var token = await SolicitarEPegarTokenAsync(cliente, email);

        using (var escopo = _fabrica.Services.CreateScope())
        {
            var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            await dbContext.RedefinicoesSenha.IgnoreQueryFilters()
                .Where(r => r.UsuarioId == usuarioId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiraEm, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        (await Redefinir(cliente, token, SenhaNova)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Usuario_desativado_nao_recebe_link()
    {
        var (_, _, usuarioId, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Recepcionista);
        using (var escopo = _fabrica.Services.CreateScope())
        {
            var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            var usuario = await dbContext.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Id == usuarioId);
            usuario.Desativar();
            await dbContext.SaveChangesAsync();
        }

        using var cliente = _fabrica.CreateClient();
        (await cliente.PostAsJsonAsync("/painel/auth/esqueci-senha", new RequisicaoEsqueciSenha(email)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Should().NotContain(e => e.Destinatario == email);
    }

    [Fact]
    public async Task Pedidos_demais_na_mesma_hora_param_de_enviar_mas_a_resposta_nao_muda()
    {
        var (_, _, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        using var cliente = _fabrica.CreateClient();

        for (var i = 0; i < ServicoRedefinicaoSenha.MaximoPedidosPorHora + 2; i++)
            (await cliente.PostAsJsonAsync("/painel/auth/esqueci-senha", new RequisicaoEsqueciSenha(email)))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);

        _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Count(e => e.Destinatario == email)
            .Should().Be(ServicoRedefinicaoSenha.MaximoPedidosPorHora);
    }

    [Fact]
    public async Task Email_aceita_maiusculas_e_espacos_e_o_link_aponta_para_o_painel()
    {
        var (_, _, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        using var cliente = _fabrica.CreateClient();

        (await cliente.PostAsJsonAsync("/painel/auth/esqueci-senha", new RequisicaoEsqueciSenha($"  {email.ToUpperInvariant()} ")))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var corpo = _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Last(e => e.Destinatario == email).CorpoHtml;
        corpo.Should().Contain($"://app.{DominioDeTeste.Valor}");
        corpo.Should().Contain("/painel/redefinir-senha?token=");
    }

    private async Task<string> SolicitarEPegarTokenAsync(HttpClient cliente, string email)
    {
        (await cliente.PostAsJsonAsync("/painel/auth/esqueci-senha", new RequisicaoEsqueciSenha(email)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var corpo = _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Last(e => e.Destinatario == email).CorpoHtml;
        var token = Regex.Match(corpo, @"redefinir-senha\?token=([A-Za-z0-9_-]+)").Groups[1].Value;
        token.Should().NotBeEmpty();
        return token;
    }

    private static Task<HttpResponseMessage> Redefinir(HttpClient cliente, string token, string senha) =>
        cliente.PostAsJsonAsync("/painel/auth/redefinir-senha", new RequisicaoRedefinirSenha(token, senha));

    private static string CookieRefresh(HttpResponseMessage resposta)
    {
        resposta.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookie = cookies!.Single(c => c.StartsWith("refresh_token="));
        return cookie[..cookie.IndexOf(';')];
    }
}
