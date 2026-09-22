using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Autenticacao;

/// <summary>
/// Sprint 1 (seção 11, M): "permissões bloqueiam ações sem acesso (teste por perfil)".
/// Autorização é sempre por permissão, nunca só por perfil (seção 4) — os testes provam
/// os três estados: sem token (401), com token mas sem a permissão (403), com a permissão certa (200).
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AutorizacaoTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public AutorizacaoTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Requisicao_sem_token_da_401()
    {
        using var cliente = _fabrica.CreateClient();
        var resposta = await cliente.GetAsync("/painel/clientes");

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Requisicao_com_token_valido_mas_sem_a_permissao_da_403()
    {
        // Perfil Profissional não recebe nenhuma permissão por padrão (seção 7).
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Profissional);

        var resposta = await cliente.GetAsync("/painel/clientes");

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Requisicao_com_a_permissao_certa_passa()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(
            Perfil.Profissional, Permissao.GerenciarClientes);

        var resposta = await cliente.GetAsync("/painel/clientes");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Administrador_tem_todas_as_permissoes_por_padrao()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var respostas = await Task.WhenAll(
            cliente.GetAsync("/painel/usuarios"),
            cliente.GetAsync("/painel/profissionais"),
            cliente.GetAsync("/painel/servicos"),
            cliente.GetAsync("/painel/clientes"),
            cliente.GetAsync("/painel/negocio"));

        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_invalido_da_401()
    {
        using var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-forjado-invalido");

        var resposta = await cliente.GetAsync("/painel/clientes");

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
