using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Profissionais;

/// <summary>
/// Foto e cor do profissional na agenda (A1-BE): <c>CorAgenda</c> opcional, só <c>#rrggbb</c> (nula = cor automática no
/// painel), editada pela ficha do profissional; a grade do dia entrega <c>FotoUrl</c> e <c>CorAgenda</c> no cabeçalho de cada coluna.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class CorFotoAgendaTestes : IAsyncLifetime
{
    private const string Foto = "https://exemplo.com/foto-ana.jpg";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public CorFotoAgendaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Profissional_sem_cor_fica_nulo_na_ficha_na_lista_e_na_grade()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarAsync(admin, new CriarProfissional("Sem Cor"));

        (await DetalheAsync(admin, id)).CorAgenda.Should().BeNull();
        (await ListaAsync(admin)).Single(p => p.Id == id).CorAgenda.Should().BeNull();
        var coluna = await ColunaDaGradeAsync(admin, id);
        coluna.GetProperty("corAgenda").ValueKind.Should().Be(JsonValueKind.Null, "nula = o painel usa a cor automática");
        coluna.GetProperty("fotoUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Criacao_com_cor_valida_normaliza_e_aparece_na_ficha_e_na_lista()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarAsync(admin, new CriarProfissional("Com Cor", CorAgenda: " #1A2B3C "));

        (await DetalheAsync(admin, id)).CorAgenda.Should().Be("#1a2b3c");
        (await ListaAsync(admin)).Single(p => p.Id == id).CorAgenda.Should().Be("#1a2b3c");
    }

    [Fact]
    public async Task Atualizacao_troca_mantem_e_remove_a_cor()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarAsync(admin, new CriarProfissional("Ana"));

        (await AtualizarAsync(admin, id, "#ABCDEF")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DetalheAsync(admin, id)).CorAgenda.Should().Be("#abcdef");

        // Payload antigo (sem o campo) não mexe na cor: contrato aditivo.
        (await admin.PutAsJsonAsync($"/painel/profissionais/{id}", new { nome = "Ana Silva", telefone = (string?)null, email = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DetalheAsync(admin, id)).Should().Match<ProfissionalDetalhe>(p => p.Nome == "Ana Silva" && p.CorAgenda == "#abcdef");

        (await AtualizarAsync(admin, id, "")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DetalheAsync(admin, id)).CorAgenda.Should().BeNull("vazio volta para a cor automática");

        var auditorias = await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .CountAsync(l => l.EntidadeId == id && l.Detalhes!.Contains("cor da agenda")));
        auditorias.Should().Be(2, "trocar e tirar a cor ficam na auditoria; o PUT que não mexeu nela, não");
    }

    public static TheoryData<string> CoresInvalidas => new()
    {
        "123456", "#123", "#12345", "#1234567", "#GGGGGG", "red", "rgb(1, 2, 3)", "linear-gradient(red, blue)",
        "var(--cor)", "#123456;background:url(x)", "#12 456",
    };

    [Theory]
    [MemberData(nameof(CoresInvalidas))]
    public async Task Cor_fora_de_RRGGBB_e_400_na_criacao_e_na_edicao_sem_gravar_nada(string invalida)
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var criar = await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Invalido", CorAgenda: invalida));
        criar.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await criar.Content.ReadAsStringAsync()).Should().Contain("#RRGGBB");
        (await _fabrica.NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().CountAsync(p => p.NegocioId == negocioId && p.Nome == "Invalido")))
            .Should().Be(0);

        var id = await CriarAsync(admin, new CriarProfissional("Valido", CorAgenda: "#123456"));
        var editar = await admin.PutAsJsonAsync($"/painel/profissionais/{id}", new AtualizarProfissional("Outro Nome", null, null, CorAgenda: invalida));
        editar.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await DetalheAsync(admin, id)).Should().Match<ProfissionalDetalhe>(p => p.Nome == "Valido" && p.CorAgenda == "#123456");
    }

    [Fact]
    public async Task Banco_recusa_cor_fora_do_formato_mesmo_por_fora_da_aplicacao()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarAsync(admin, new CriarProfissional("Check"));

        var acao = () => _fabrica.NoBancoAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET cor_agenda = {"red"} WHERE id = {id}"));

        await acao.Should().ThrowAsync<Exception>();
        (await DetalheAsync(admin, id)).CorAgenda.Should().BeNull();
    }

    [Fact]
    public async Task Grade_traz_foto_e_cor_de_cada_coluna_sem_dados_pessoais()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var comCor = await CriarAsync(admin, new CriarProfissional("Ana Grade", "+5571988887777", "ana@teste.com", CorAgenda: "#FF8800"));
        (await admin.PutAsJsonAsync($"/painel/profissionais/{comCor}",
            new AtualizarProfissional("Ana Grade", "+5571988887777", "ana@teste.com", FotoUrl: Foto))).EnsureSuccessStatusCode();
        var semCor = await CriarAsync(admin, new CriarProfissional("Bia Grade"));

        var colunaComCor = await ColunaDaGradeAsync(admin, comCor);
        colunaComCor.GetProperty("fotoUrl").GetString().Should().Be(Foto);
        colunaComCor.GetProperty("corAgenda").GetString().Should().Be("#ff8800");
        (await ColunaDaGradeAsync(admin, semCor)).GetProperty("corAgenda").ValueKind.Should().Be(JsonValueKind.Null);

        var texto = colunaComCor.GetRawText();
        texto.Should().NotContain("ana@teste.com").And.NotContain("988887777").And.NotContain("cpf", "a grade não expõe dado pessoal");
    }

    [Fact]
    public async Task Desativar_e_reativar_preservam_a_cor()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarAsync(admin, new CriarProfissional("Vai e Volta", CorAgenda: "#336699"));

        (await admin.PostAsync($"/painel/profissionais/{id}/desativar", null)).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/painel/profissionais/{id}/ativar", null)).EnsureSuccessStatusCode();

        (await DetalheAsync(admin, id)).Should().Match<ProfissionalDetalhe>(p => p.Ativo && p.CorAgenda == "#336699");
    }

    [Fact]
    public async Task Outro_negocio_nao_le_nem_altera_a_cor()
    {
        var (adminA, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (adminB, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarAsync(adminA, new CriarProfissional("Do Negocio A", CorAgenda: "#111111"));

        (await adminB.GetAsync($"/painel/profissionais/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AtualizarAsync(adminB, id, "#222222")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListaAsync(adminB)).Should().NotContain(p => p.Id == id);
        var grade = await GradeAsync(adminB);
        grade.GetProperty("profissionais").EnumerateArray().Should().NotContain(c => c.GetProperty("profissionalId").GetGuid() == id);

        (await DetalheAsync(adminA, id)).CorAgenda.Should().Be("#111111");
    }

    [Fact]
    public async Task So_quem_gerencia_profissionais_altera_a_cor()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarAsync(admin, new CriarProfissional("Protegida", CorAgenda: "#123456"));
        var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista);

        (await AtualizarAsync(recepcao, id, "#654321")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DetalheAsync(admin, id)).CorAgenda.Should().Be("#123456");
    }

    // ---------------------------------------------------------------- apoio

    private static async Task<Guid> CriarAsync(HttpClient cliente, CriarProfissional dados)
    {
        var resposta = await cliente.PostAsJsonAsync("/painel/profissionais", dados);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    private static Task<HttpResponseMessage> AtualizarAsync(HttpClient cliente, Guid id, string cor) =>
        cliente.PutAsJsonAsync($"/painel/profissionais/{id}", new AtualizarProfissional("Ana", null, null, CorAgenda: cor));

    private static async Task<ProfissionalDetalhe> DetalheAsync(HttpClient cliente, Guid id) =>
        (await cliente.GetFromJsonAsync<ProfissionalDetalhe>($"/painel/profissionais/{id}"))!;

    private static async Task<List<ProfissionalResumo>> ListaAsync(HttpClient cliente) =>
        (await cliente.GetFromJsonAsync<List<ProfissionalResumo>>("/painel/profissionais"))!;

    private static async Task<JsonElement> GradeAsync(HttpClient cliente)
    {
        var resposta = await cliente.GetAsync($"/painel/agenda/grade?data={SemeadorDeComissoes.Dia(3):yyyy-MM-dd}");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
    }

    private static async Task<JsonElement> ColunaDaGradeAsync(HttpClient cliente, Guid profissionalId) =>
        (await GradeAsync(cliente)).GetProperty("profissionais").EnumerateArray()
            .Single(c => c.GetProperty("profissionalId").GetGuid() == profissionalId);
}
