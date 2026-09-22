using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Servicos;

[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ServicosECategoriasTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public ServicosECategoriasTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Criar_categoria_e_servico_populado_do_grupo_mais_procurados()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var respostaCategoria = await cliente.PostAsJsonAsync(
            "/painel/categorias", new CriarCategoriaRequisicaoTeste("Cabelo"));
        respostaCategoria.StatusCode.Should().Be(HttpStatusCode.Created);
        var categoriaId = await respostaCategoria.Content.ReadFromJsonAsync<Guid>();

        var respostaServico = await cliente.PostAsJsonAsync("/painel/servicos", new CriarServico(
            categoriaId, "Corte Masculino", 45m, 30, Popular: true));
        respostaServico.StatusCode.Should().Be(HttpStatusCode.Created);

        var servicos = await cliente.GetFromJsonAsync<List<ServicoResumo>>("/painel/servicos");
        servicos.Should().ContainSingle(s => s.Nome == "Corte Masculino" && s.Popular && s.CategoriaId == categoriaId);

        var categorias = await cliente.GetFromJsonAsync<List<CategoriaResumo>>("/painel/categorias");
        categorias.Should().ContainSingle(c => c.Id == categoriaId && c.Nome == "Cabelo" && c.Ativa);
    }

    [Fact]
    public async Task Desativar_e_ativar_servico_funciona()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var categoriaId = await (await cliente.PostAsJsonAsync(
            "/painel/categorias", new CriarCategoriaRequisicaoTeste("Unhas"))).Content.ReadFromJsonAsync<Guid>();

        var servicoId = await (await cliente.PostAsJsonAsync(
            "/painel/servicos", new CriarServico(categoriaId, "Manicure", 30m, 40))).Content.ReadFromJsonAsync<Guid>();

        (await cliente.PostAsync($"/painel/servicos/{servicoId}/desativar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var servicos = await cliente.GetFromJsonAsync<List<ServicoResumo>>("/painel/servicos");
        servicos.Should().ContainSingle(s => s.Id == servicoId && !s.Ativo);
    }

    private sealed record CriarCategoriaRequisicaoTeste(string Nome);
}
