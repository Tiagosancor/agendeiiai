using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Usuarios;

/// <summary>Sprint 1 (seção 11, M): "CPF nunca aparece em texto puro no banco nem nos logs".</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class UsuariosControllerTestes : IAsyncLifetime
{
    private const string CpfDeTeste = "111.444.777-35";
    private const string CpfDigitos = "11144477735";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public UsuariosControllerTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Criar_usuario_com_cpf_nunca_guarda_o_cpf_em_texto_puro_no_banco()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var resposta = await cliente.PostAsJsonAsync("/painel/usuarios", new CriarUsuario(
            "Novo Usuário", "novo@teste.com", "SenhaForte!123", Perfil.Recepcionista, Cpf: CpfDeTeste));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        // Varre TODAS as colunas de texto da tabela usuarios, incluindo o bytea convertido
        // para texto — o CPF em dígitos puros não pode aparecer em lugar nenhum.
        await using var conexao = new NpgsqlConnection(_postgres.ConnectionString);
        await conexao.OpenAsync();

        await using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT * FROM usuarios WHERE email = 'novo@teste.com'";
        await using var leitor = await comando.ExecuteReaderAsync();

        (await leitor.ReadAsync()).Should().BeTrue();

        for (var i = 0; i < leitor.FieldCount; i++)
        {
            if (await leitor.IsDBNullAsync(i))
                continue;

            var valorComoTexto = leitor.GetValue(i) switch
            {
                byte[] bytes => Convert.ToBase64String(bytes) + BitConverter.ToString(bytes),
                var outro => outro.ToString(),
            };

            valorComoTexto.Should().NotContain(CpfDigitos, $"a coluna '{leitor.GetName(i)}' não pode conter o CPF em texto puro");
        }
    }

    [Fact]
    public async Task Cpf_mascarado_aparece_no_detalhe_mas_os_digitos_completos_so_no_endpoint_dedicado()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var respostaCriar = await cliente.PostAsJsonAsync("/painel/usuarios", new CriarUsuario(
            "Novo Usuário", "novo2@teste.com", "SenhaForte!123", Perfil.Recepcionista, Cpf: CpfDeTeste));
        var id = await respostaCriar.Content.ReadFromJsonAsync<Guid>();

        var detalhe = await cliente.GetFromJsonAsync<UsuarioDetalhe>($"/painel/usuarios/{id}", OpcoesJsonTeste.Opcoes);
        detalhe!.CpfMascarado.Should().Be("***.444.777-**");

        var cpfCompleto = await cliente.GetStringAsync($"/painel/usuarios/{id}/cpf");
        cpfCompleto.Trim('"').Should().Be(CpfDigitos);
    }

    [Fact]
    public async Task Criar_usuario_com_email_repetido_da_409()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var primeiraResposta = await cliente.PostAsJsonAsync("/painel/usuarios", new CriarUsuario(
            "Fulano", "duplicado@teste.com", "SenhaForte!123", Perfil.Recepcionista));
        primeiraResposta.StatusCode.Should().Be(HttpStatusCode.Created);

        var segundaResposta = await cliente.PostAsJsonAsync("/painel/usuarios", new CriarUsuario(
            "Sicrano", "duplicado@teste.com", "OutraSenha!123", Perfil.Recepcionista));

        segundaResposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Conceder_e_revogar_permissao_funciona()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var respostaCriar = await cliente.PostAsJsonAsync("/painel/usuarios", new CriarUsuario(
            "Fulano", "permissoes@teste.com", "SenhaForte!123", Perfil.Profissional));
        var id = await respostaCriar.Content.ReadFromJsonAsync<Guid>();

        (await cliente.GetFromJsonAsync<UsuarioDetalhe>($"/painel/usuarios/{id}", OpcoesJsonTeste.Opcoes))!
            .Permissoes.Should().BeEmpty();

        (await cliente.PostAsync($"/painel/usuarios/{id}/permissoes/{Permissao.GerenciarClientes}", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await cliente.GetFromJsonAsync<UsuarioDetalhe>($"/painel/usuarios/{id}", OpcoesJsonTeste.Opcoes))!
            .Permissoes.Should().Contain(Permissao.GerenciarClientes);

        (await cliente.DeleteAsync($"/painel/usuarios/{id}/permissoes/{Permissao.GerenciarClientes}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await cliente.GetFromJsonAsync<UsuarioDetalhe>($"/painel/usuarios/{id}", OpcoesJsonTeste.Opcoes))!
            .Permissoes.Should().NotContain(Permissao.GerenciarClientes);
    }

    [Fact]
    public async Task Desativar_e_ativar_usuario_funciona()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var respostaCriar = await cliente.PostAsJsonAsync("/painel/usuarios", new CriarUsuario(
            "Fulano", "ativo@teste.com", "SenhaForte!123", Perfil.Profissional));
        var id = await respostaCriar.Content.ReadFromJsonAsync<Guid>();

        (await cliente.PostAsync($"/painel/usuarios/{id}/desativar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.GetFromJsonAsync<UsuarioDetalhe>($"/painel/usuarios/{id}", OpcoesJsonTeste.Opcoes))!.Ativo.Should().BeFalse();

        (await cliente.PostAsync($"/painel/usuarios/{id}/ativar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.GetFromJsonAsync<UsuarioDetalhe>($"/painel/usuarios/{id}", OpcoesJsonTeste.Opcoes))!.Ativo.Should().BeTrue();
    }
}
