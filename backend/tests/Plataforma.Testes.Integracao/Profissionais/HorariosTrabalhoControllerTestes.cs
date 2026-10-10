using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Profissionais;

/// <summary>
/// Ajuste pós-Sprint 5 (seção 14 do prompt-do-projeto.md, "Replicar para..."): ao escrever
/// o teste de e2e do painel para essa funcionalidade, o GET de horários de trabalho
/// devolvia 500 sempre que o profissional já tinha pelo menos um intervalo salvo — bug
/// pré-existente, nunca coberto por teste (nenhum teste de integração exercitava GET
/// depois de um PUT com dados de verdade). Ver docs/decisoes.md.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class HorariosTrabalhoControllerTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public HorariosTrabalhoControllerTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Definir_e_depois_listar_horarios_devolve_os_intervalos_salvos_em_ordem_de_semana()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var respostaCriar = await cliente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional(
            "Profissional Horarios", null, null, Cpf: null));
        var profissionalId = await respostaCriar.Content.ReadFromJsonAsync<Guid>();

        // Salva fora de ordem de propósito (Sexta antes de Segunda) — a listagem precisa
        // devolver em ordem de semana (Domingo..Sábado), não na ordem em que foi gravado
        // nem em ordem alfabética do nome do dia em português.
        var intervalos = new[]
        {
            new IntervaloTrabalho(5, new TimeOnly(9, 0), new TimeOnly(18, 0)), // Sexta
            new IntervaloTrabalho(1, new TimeOnly(9, 0), new TimeOnly(12, 0)), // Segunda (manhã)
            new IntervaloTrabalho(1, new TimeOnly(13, 0), new TimeOnly(18, 0)), // Segunda (tarde, com almoço)
        };

        (await cliente.PutAsJsonAsync($"/painel/profissionais/{profissionalId}/horarios", intervalos))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var respostaListar = await cliente.GetAsync($"/painel/profissionais/{profissionalId}/horarios");
        respostaListar.StatusCode.Should().Be(HttpStatusCode.OK);

        var salvos = await respostaListar.Content.ReadFromJsonAsync<List<IntervaloTrabalho>>();
        salvos.Should().HaveCount(3);
        salvos.Should().BeInAscendingOrder(i => i.DiaSemana);
        salvos![0].DiaSemana.Should().Be(1);
        salvos[0].Inicio.Should().Be(new TimeOnly(9, 0));
        salvos[2].DiaSemana.Should().Be(5);
    }

    public static TheoryData<TimeOnly, TimeOnly> IntervalosInvalidos => new()
    {
        { new TimeOnly(9, 0), new TimeOnly(9, 0) },
        { new TimeOnly(10, 0), new TimeOnly(9, 0) },
        { new TimeOnly(22, 0), new TimeOnly(2, 0) },
    };

    [Theory]
    [MemberData(nameof(IntervalosInvalidos))]
    public async Task Intervalo_invalido_devolve_400_seguro_sem_gravar_nem_alterar_outro_tenant(
        TimeOnly inicio, TimeOnly fim)
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await CriarProfissionalAsync(cliente, "Profissional do tenant principal");
        var horarioOriginal = new IntervaloTrabalho(1, new TimeOnly(9, 0), new TimeOnly(18, 0));
        (await cliente.PutAsJsonAsync($"/painel/profissionais/{profissionalId}/horarios", new[] { horarioOriginal }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var (clienteOutroTenant, outroNegocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var outroProfissionalId = await CriarProfissionalAsync(clienteOutroTenant, "Profissional de outro tenant");
        var horarioOutroTenant = new IntervaloTrabalho(2, new TimeOnly(10, 0), new TimeOnly(16, 0));
        (await clienteOutroTenant.PutAsJsonAsync(
            $"/painel/profissionais/{outroProfissionalId}/horarios", new[] { horarioOutroTenant }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resposta = await cliente.PutAsJsonAsync($"/painel/profissionais/{profissionalId}/horarios",
            new[] { new IntervaloTrabalho(1, inicio, fim) });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problema = await resposta.Content.ReadFromJsonAsync<ProblemDetails>();
        problema.Should().NotBeNull();
        problema!.Status.Should().Be(StatusCodes.Status400BadRequest);
        problema.Title.Should().Be("O fim do intervalo precisa ser depois do início.");
        (await resposta.Content.ReadAsStringAsync()).Should().NotContain("Parameter").And.NotContain("stack");

        var horarios = await _fabrica.NoBancoAsync(db => db.HorariosTrabalho.IgnoreQueryFilters()
            .Where(h => h.ProfissionalId == profissionalId || h.ProfissionalId == outroProfissionalId)
            .OrderBy(h => h.ProfissionalId)
            .Select(h => new { h.NegocioId, h.ProfissionalId, h.DiaSemana, h.Inicio, h.Fim })
            .ToListAsync());

        horarios.Should().HaveCount(2);
        horarios.Should().ContainSingle(h => h.NegocioId == negocioId && h.ProfissionalId == profissionalId
            && h.DiaSemana == DiaSemana.Segunda && h.Inicio == horarioOriginal.Inicio && h.Fim == horarioOriginal.Fim);
        horarios.Should().ContainSingle(h => h.NegocioId == outroNegocioId && h.ProfissionalId == outroProfissionalId
            && h.DiaSemana == DiaSemana.Terca && h.Inicio == horarioOutroTenant.Inicio && h.Fim == horarioOutroTenant.Fim);
    }

    private static async Task<Guid> CriarProfissionalAsync(HttpClient cliente, string nome)
    {
        var resposta = await cliente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional(nome, null, null, Cpf: null));
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<Guid>());
    }
}
