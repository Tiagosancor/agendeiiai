using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Clientes;

[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ClientesControllerTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public ClientesControllerTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Criar_e_obter_cliente()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var resposta = await cliente.PostAsJsonAsync("/painel/clientes", new CriarCliente(
            "João Cliente", "+5571988887777", "joao@teste.com"));
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await resposta.Content.ReadFromJsonAsync<Guid>();

        var detalhe = await cliente.GetFromJsonAsync<ClienteResumo>($"/painel/clientes/{id}");
        detalhe!.Nome.Should().Be("João Cliente");
        detalhe.Telefone.Should().Be("+5571988887777");
    }

    [Fact]
    public async Task Criar_cliente_com_telefone_repetido_no_mesmo_negocio_da_409()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        (await cliente.PostAsJsonAsync("/painel/clientes", new CriarCliente("João", "+5571988887777")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var segunda = await cliente.PostAsJsonAsync("/painel/clientes", new CriarCliente("Outro João", "+5571988887777"));
        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Mesmo_telefone_em_negocios_diferentes_e_permitido()
    {
        var (clienteA, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (clienteB, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        (await clienteA.PostAsJsonAsync("/painel/clientes", new CriarCliente("João", "+5571988887777")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await clienteB.PostAsJsonAsync("/painel/clientes", new CriarCliente("João de outro negócio", "+5571988887777")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Atualizar_cliente_funciona()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var id = await (await cliente.PostAsJsonAsync("/painel/clientes", new CriarCliente("João", "+5571988887777")))
            .Content.ReadFromJsonAsync<Guid>();

        (await cliente.PutAsJsonAsync($"/painel/clientes/{id}", new AtualizarCliente("João Silva", "joao@novo.com", "Cliente fiel")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var detalhe = await cliente.GetFromJsonAsync<ClienteResumo>($"/painel/clientes/{id}");
        detalhe!.Nome.Should().Be("João Silva");
        detalhe.Observacoes.Should().Be("Cliente fiel");
    }
}
