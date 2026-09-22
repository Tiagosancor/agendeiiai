using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Profissionais;

[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ProfissionaisControllerTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public ProfissionaisControllerTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Criar_listar_atualizar_e_desativar_profissional()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var respostaCriar = await cliente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional(
            "Maria Barbeira", "+5571988887777", "maria@teste.com", Cpf: "111.444.777-35"));
        respostaCriar.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await respostaCriar.Content.ReadFromJsonAsync<Guid>();

        var lista = await cliente.GetFromJsonAsync<List<ProfissionalResumo>>("/painel/profissionais");
        lista.Should().ContainSingle(p => p.Id == id && p.Nome == "Maria Barbeira");

        var detalhe = await cliente.GetFromJsonAsync<ProfissionalDetalhe>($"/painel/profissionais/{id}");
        detalhe!.CpfMascarado.Should().Be("***.444.777-**");

        (await cliente.PutAsJsonAsync($"/painel/profissionais/{id}", new AtualizarProfissional(
            "Maria B. Silva", "+5571988887778", "maria2@teste.com"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await cliente.GetFromJsonAsync<ProfissionalDetalhe>($"/painel/profissionais/{id}"))!
            .Nome.Should().Be("Maria B. Silva");

        (await cliente.PostAsync($"/painel/profissionais/{id}/desativar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.GetFromJsonAsync<ProfissionalDetalhe>($"/painel/profissionais/{id}"))!.Ativo.Should().BeFalse();
    }

    [Fact]
    public async Task Cpf_do_profissional_nunca_fica_em_texto_puro_no_banco()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        await cliente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional(
            "Carlos Barbeiro", Cpf: "111.444.777-35"));

        await using var conexao = new NpgsqlConnection(_postgres.ConnectionString);
        await conexao.OpenAsync();
        await using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT cpf_texto_cifrado, cpf_mascarado FROM profissionais WHERE nome = 'Carlos Barbeiro'";
        await using var leitor = await comando.ExecuteReaderAsync();

        (await leitor.ReadAsync()).Should().BeTrue();
        var textoCifrado = (byte[])leitor["cpf_texto_cifrado"];
        Convert.ToBase64String(textoCifrado).Should().NotContain("11144477735");
        leitor["cpf_mascarado"].Should().Be("***.444.777-**");
    }
}
