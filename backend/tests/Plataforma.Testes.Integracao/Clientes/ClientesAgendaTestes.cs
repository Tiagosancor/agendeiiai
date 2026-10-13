using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Clientes;

/// <summary>Busca e cadastro rápido de cliente na agenda (<c>/painel/agenda/clientes</c>).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ClientesAgendaTestes : IAsyncLifetime
{
    private const string Busca = "/painel/agenda/clientes/busca";
    private const string Rapido = "/painel/agenda/clientes/rapido";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public ClientesAgendaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private static async Task<Guid> CriarAsync(HttpClient cliente, string nome, string? telefone, string? email = null)
    {
        var resposta = await cliente.PostAsJsonAsync("/painel/clientes", new CriarCliente(nome, telefone, email));
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<List<ClienteBusca>> BuscarAsync(HttpClient cliente, string termo, int? limite = null)
    {
        var url = $"{Busca}?termo={Uri.EscapeDataString(termo)}" + (limite is null ? "" : $"&limite={limite}");
        var resposta = await cliente.GetAsync(url);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resposta.Content.ReadFromJsonAsync<List<ClienteBusca>>())!;
    }

    private async Task<HttpClient> AdministradorAsync() => (await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador)).Cliente;

    // ---- Busca por nome ----

    [Theory]
    [InlineData("joao")]
    [InlineData("JOAO")]
    [InlineData("João")]
    [InlineData("jOãO")]
    public async Task Nome_acha_sem_diferenciar_acento_nem_maiuscula(string termo)
    {
        var cliente = await AdministradorAsync();
        var joao = await CriarAsync(cliente, "João da Silva", "+5571988887777");
        await CriarAsync(cliente, "Maria Souza", "+5571988886666");

        var achados = await BuscarAsync(cliente, termo);

        achados.Should().ContainSingle().Which.Id.Should().Be(joao);
    }

    [Fact]
    public async Task Nome_sem_acento_no_termo_acha_cadastro_acentuado_e_o_contrario()
    {
        var cliente = await AdministradorAsync();
        await CriarAsync(cliente, "Zélia Conceição", "+5571988887777");
        await CriarAsync(cliente, "Andre Lima", "+5571988886666");

        (await BuscarAsync(cliente, "conceicao")).Should().ContainSingle().Which.Nome.Should().Be("Zélia Conceição");
        (await BuscarAsync(cliente, "zélia")).Should().ContainSingle();
        (await BuscarAsync(cliente, "andré")).Should().ContainSingle().Which.Nome.Should().Be("Andre Lima");
    }

    [Fact]
    public async Task Nome_aceita_trecho_do_meio_e_ordena_por_relevancia_depois_por_nome()
    {
        var cliente = await AdministradorAsync();
        await CriarAsync(cliente, "Maria Silva", "+5571988880001");
        await CriarAsync(cliente, "João da Silva", "+5571988880002");
        await CriarAsync(cliente, "Silvana Rocha", "+5571988880003");
        await CriarAsync(cliente, "Silvio Alves", "+5571988880004");
        await CriarAsync(cliente, "Pedro Souza", "+5571988880005");

        var achados = await BuscarAsync(cliente, "silv");

        // Quem começa com o termo primeiro (por nome), depois quem só contém (por nome).
        achados.Select(c => c.Nome).Should().Equal("Silvana Rocha", "Silvio Alves", "João da Silva", "Maria Silva");
    }

    [Fact]
    public async Task Curinga_do_like_no_termo_e_tratado_como_texto()
    {
        var cliente = await AdministradorAsync();
        await CriarAsync(cliente, "Ana Paula", "+5571988880001");
        await CriarAsync(cliente, "Bruna_Lima", "+5571988880002");

        (await BuscarAsync(cliente, "a%")).Should().BeEmpty();
        (await BuscarAsync(cliente, "_")).Should().BeEmpty();
        (await BuscarAsync(cliente, "na_l")).Should().ContainSingle().Which.Nome.Should().Be("Bruna_Lima");
        (await BuscarAsync(cliente, "a_a")).Should().BeEmpty();
    }

    // ---- Busca por telefone ----

    [Theory]
    [InlineData("(71) 98888-7777")]
    [InlineData("71988887777")]
    [InlineData("71 98888 7777")]
    [InlineData("+55 71 98888-7777")]
    [InlineData("98888-7777")]
    [InlineData("7777")]
    [InlineData("777")]
    public async Task Telefone_acha_com_ou_sem_mascara_e_pelos_ultimos_digitos(string termo)
    {
        var cliente = await AdministradorAsync();
        var id = await CriarAsync(cliente, "Carla Dias", "+5571988887777");
        await CriarAsync(cliente, "Outra Pessoa", "+5571988881234");

        var achados = await BuscarAsync(cliente, termo);

        achados.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public async Task Telefone_com_menos_de_tres_digitos_nao_e_tratado_como_telefone()
    {
        var cliente = await AdministradorAsync();
        await CriarAsync(cliente, "Carla Dias", "+5571988887777");

        (await BuscarAsync(cliente, "77")).Should().BeEmpty();
        (await BuscarAsync(cliente, "(7)7")).Should().BeEmpty();
    }

    [Fact]
    public async Task Fim_do_telefone_vem_antes_de_quem_so_contem_os_digitos()
    {
        var cliente = await AdministradorAsync();
        var termina = await CriarAsync(cliente, "Zeca Termina", "+5571988881234");
        var contem = await CriarAsync(cliente, "Ana Contem", "+5571912349999");

        var achados = await BuscarAsync(cliente, "1234");

        achados.Select(c => c.Id).Should().Equal(termina, contem);
    }

    [Fact]
    public async Task Cliente_sem_telefone_e_achado_so_pelo_nome()
    {
        var cliente = await AdministradorAsync();
        var id = await CriarAsync(cliente, "Balcão Sem Fone", null);

        var achados = await BuscarAsync(cliente, "balcao");

        achados.Should().ContainSingle().Which.Should().Match<ClienteBusca>(c => c.Id == id && c.Telefone == null);
    }

    // ---- Limites e formato ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData(" j ")]
    [InlineData("7")]
    public async Task Termo_com_menos_de_dois_caracteres_devolve_vazio(string? termo)
    {
        var cliente = await AdministradorAsync();
        await CriarAsync(cliente, "João", "+5571988887777");

        var url = termo is null ? Busca : $"{Busca}?termo={Uri.EscapeDataString(termo)}";
        var resposta = await cliente.GetAsync(url);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<List<ClienteBusca>>()).Should().BeEmpty();
    }

    [Fact]
    public async Task Limite_e_respeitado_padrao_10_e_maximo_20()
    {
        var cliente = await AdministradorAsync();
        for (var i = 1; i <= 25; i++)
            await CriarAsync(cliente, $"Teste {i:D2}", $"+55719888800{i:D2}");

        (await BuscarAsync(cliente, "teste")).Should().HaveCount(10);
        (await BuscarAsync(cliente, "teste", limite: 3)).Should().HaveCount(3);
        (await BuscarAsync(cliente, "teste", limite: 20)).Should().HaveCount(20);
        (await BuscarAsync(cliente, "teste", limite: 100)).Should().HaveCount(20);
        (await BuscarAsync(cliente, "teste", limite: 0)).Should().HaveCount(1);
        (await BuscarAsync(cliente, "teste", limite: -5)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Resposta_traz_so_id_nome_telefone_e_email()
    {
        var cliente = await AdministradorAsync();
        await CriarAsync(cliente, "João", "+5571988887777", "joao@teste.com");

        var json = await (await cliente.GetAsync($"{Busca}?termo=joao")).Content.ReadAsStringAsync();
        var item = JsonDocument.Parse(json).RootElement[0];

        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "nome", "telefone", "email");
        item.GetProperty("email").GetString().Should().Be("joao@teste.com");
        item.GetProperty("telefone").GetString().Should().Be("+5571988887777");
    }

    [Fact]
    public async Task Cliente_excluido_anonimizado_nao_aparece()
    {
        var cliente = await AdministradorAsync();
        var id = await CriarAsync(cliente, "Pessoa Removida", "+5571988887777");
        (await cliente.PostAsync($"/painel/clientes/{id}/excluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await BuscarAsync(cliente, "removida")).Should().BeEmpty();
        (await BuscarAsync(cliente, "8888")).Should().BeEmpty();
    }

    // ---- Multi-tenant e autorização ----

    [Fact]
    public async Task Busca_nao_vaza_clientes_de_outro_negocio()
    {
        var a = await AdministradorAsync();
        var b = await AdministradorAsync();
        await CriarAsync(a, "João do Negócio A", "+5571988887777");
        var deB = await CriarAsync(b, "João do Negócio B", "+5571988886666");

        (await BuscarAsync(a, "joao")).Select(c => c.Nome).Should().Equal("João do Negócio A");
        (await BuscarAsync(b, "joao")).Should().ContainSingle().Which.Id.Should().Be(deB);
        (await BuscarAsync(a, "6666")).Should().BeEmpty();
        (await BuscarAsync(b, "7777")).Should().BeEmpty();
    }

    [Fact]
    public async Task Sem_login_da_401()
    {
        using var anonimo = _fabrica.CreateClient();

        (await anonimo.GetAsync($"{Busca}?termo=joao")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.PostAsJsonAsync(Rapido, new CadastroRapidoCliente("João", "71988887777"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sem_permissao_de_agenda_da_403()
    {
        var (profissional, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Profissional);

        (await profissional.GetAsync($"{Busca}?termo=joao")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await profissional.PostAsJsonAsync(Rapido, new CadastroRapidoCliente("João", "71988887777"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Recepcionista_usa_busca_e_cadastro_rapido()
    {
        var (recepcao, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Recepcionista);

        (await recepcao.PostAsJsonAsync(Rapido, new CadastroRapidoCliente("Rita Recepção", "71988887777"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await BuscarAsync(recepcao, "rita")).Should().ContainSingle();
    }

    // ---- Cadastro rápido ----

    [Fact]
    public async Task Cadastro_rapido_valido_cria_com_telefone_normalizado_e_padroes_do_cadastro_completo()
    {
        var cliente = await AdministradorAsync();

        var resposta = await cliente.PostAsJsonAsync(Rapido, new CadastroRapidoCliente("  Paulo Novo  ", "(71) 98888-7777", "paulo@teste.com"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var criado = (await resposta.Content.ReadFromJsonAsync<ClienteBusca>())!;
        criado.Nome.Should().Be("Paulo Novo");
        criado.Telefone.Should().Be("+5571988887777");
        criado.Email.Should().Be("paulo@teste.com");

        var completo = await cliente.GetFromJsonAsync<ClienteResumo>($"/painel/clientes/{criado.Id}");
        completo!.AceitaLembreteRetorno.Should().BeTrue();
        completo.Excluido.Should().BeFalse();

        (await BuscarAsync(cliente, "paulo")).Should().ContainSingle().Which.Id.Should().Be(criado.Id);
    }

    [Fact]
    public async Task Cadastro_rapido_sem_email_e_aceito()
    {
        var cliente = await AdministradorAsync();

        foreach (var email in new string?[] { null, "", "   " })
        {
            var resposta = await cliente.PostAsJsonAsync(Rapido, new CadastroRapidoCliente("Sem Email " + Guid.NewGuid().ToString("N")[..4], $"7198888{Random.Shared.Next(1000, 9999)}", email));
            resposta.StatusCode.Should().Be(HttpStatusCode.Created);
            (await resposta.Content.ReadFromJsonAsync<ClienteBusca>())!.Email.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(null, "71988887777", "Informe o nome")]
    [InlineData("", "71988887777", "Informe o nome")]
    [InlineData("   ", "71988887777", "Informe o nome")]
    [InlineData("João", null, "Informe o telefone")]
    [InlineData("João", "", "Informe o telefone")]
    [InlineData("João", "abc", "Telefone inválido")]
    [InlineData("João", "123", "Telefone inválido")]
    [InlineData("João", "7198888", "Telefone inválido")]
    public async Task Cadastro_rapido_com_dados_invalidos_da_400_sem_criar(string? nome, string? telefone, string mensagem)
    {
        var cliente = await AdministradorAsync();

        var resposta = await cliente.PostAsJsonAsync(Rapido, new CadastroRapidoCliente(nome, telefone));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain(mensagem);
        (await cliente.GetFromJsonAsync<List<ClienteResumo>>("/painel/clientes"))!.Should().BeEmpty();
    }

    [Theory]
    [InlineData("semarroba")]
    [InlineData("a@")]
    [InlineData("@teste.com")]
    [InlineData("a@b")]
    [InlineData("a b@teste.com")]
    public async Task Cadastro_rapido_com_email_invalido_da_400(string email)
    {
        var cliente = await AdministradorAsync();

        var resposta = await cliente.PostAsJsonAsync(Rapido, new CadastroRapidoCliente("João", "71988887777", email));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("E-mail inválido");
    }

    [Fact]
    public async Task Cadastro_rapido_com_nome_gigante_da_400()
    {
        var cliente = await AdministradorAsync();

        var resposta = await cliente.PostAsJsonAsync(Rapido, new CadastroRapidoCliente(new string('a', 151), "71988887777"));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("71988887777")]
    [InlineData("(71) 98888-7777")]
    [InlineData("+5571988887777")]
    [InlineData("55 71 98888 7777")]
    public async Task Telefone_repetido_da_409_com_o_cliente_existente_em_qualquer_formato(string telefoneNovo)
    {
        var cliente = await AdministradorAsync();
        var id = await CriarAsync(cliente, "Quem Já Existe", "+5571988887777", "existe@teste.com");

        var resposta = await cliente.PostAsJsonAsync(Rapido, new CadastroRapidoCliente("Outro Nome", telefoneNovo, "outro@teste.com"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        corpo.GetProperty("id").GetGuid().Should().Be(id);
        corpo.GetProperty("nome").GetString().Should().Be("Quem Já Existe");
        corpo.GetProperty("telefone").GetString().Should().Be("+5571988887777");
        corpo.GetProperty("email").GetString().Should().Be("existe@teste.com");
        corpo.GetProperty("codigo").GetString().Should().Be("telefone_ja_cadastrado");

        (await cliente.GetFromJsonAsync<List<ClienteResumo>>("/painel/clientes"))!.Should().ContainSingle();
    }

    [Fact]
    public async Task Mesmo_telefone_em_outro_negocio_cria_normalmente_e_o_409_nunca_mostra_cliente_do_outro()
    {
        var a = await AdministradorAsync();
        var b = await AdministradorAsync();
        await CriarAsync(a, "Cliente do A", "+5571988887777");

        var emB = await b.PostAsJsonAsync(Rapido, new CadastroRapidoCliente("Cliente do B", "71988887777"));

        emB.StatusCode.Should().Be(HttpStatusCode.Created);
        (await emB.Content.ReadFromJsonAsync<ClienteBusca>())!.Nome.Should().Be("Cliente do B");

        var repetidoEmB = await b.PostAsJsonAsync(Rapido, new CadastroRapidoCliente("Outro do B", "71988887777"));
        repetidoEmB.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await repetidoEmB.Content.ReadAsStringAsync()).Should().Contain("Cliente do B").And.NotContain("Cliente do A");
    }

    [Fact]
    public async Task Dois_cadastros_simultaneos_do_mesmo_telefone_criam_um_so()
    {
        var cliente = await AdministradorAsync();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            cliente.PostAsJsonAsync(Rapido, new CadastroRapidoCliente($"Corrida {i}", "71988887777"))));

        respostas.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(5);
        (await cliente.GetFromJsonAsync<List<ClienteResumo>>("/painel/clientes"))!.Should().ContainSingle();
    }
}
