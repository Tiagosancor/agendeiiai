using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Controllers.Publico;
using Plataforma.Aplicacao.Publico;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Publico;

/// <summary>
/// Catálogo do assistente com o profissional primeiro e a vitrine da página (seções 6.1 e 6.2, item 14 da seção 14):
/// serviços só de quem está ativo, preço/duração do profissional e <c>ExibirNaPaginaInicial</c>.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class CatalogoPublicoTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public CatalogoPublicoTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Catalogo_traz_so_o_que_a_equipe_ativa_executa_com_o_preco_e_a_duracao_de_cada_profissional()
    {
        var c = await SemearAsync();
        using var cliente = ClientePara(c.Slug);

        var servicos = (await cliente.GetFromJsonAsync<List<CategoriaComServicosPublicos>>("/publico/servicos"))!.SelectMany(g => g.Servicos).ToList();
        servicos.Select(s => s.Id).Should().BeEquivalentTo([c.Corte, c.Barba, c.ForaDaVitrine]);
        servicos.Single(s => s.Id == c.ForaDaVitrine).ExibirNaPaginaInicial.Should().BeFalse();
        servicos.Single(s => s.Id == c.Corte).ExibirNaPaginaInicial.Should().BeTrue();

        var profissionais = (await cliente.GetFromJsonAsync<List<ProfissionalPublico>>("/publico/profissionais"))!;
        profissionais.Select(p => p.Id).Should().BeEquivalentTo([c.Ana, c.Beto]);
        var corteDaAna = profissionais.Single(p => p.Id == c.Ana).Servicos!.Single(s => s.ServicoId == c.Corte);
        (corteDaAna.Preco, corteDaAna.DuracaoMinutos).Should().Be((70m, 60));
        var corteDoBeto = profissionais.Single(p => p.Id == c.Beto).Servicos!.Single(s => s.ServicoId == c.Corte);
        (corteDoBeto.Preco, corteDoBeto.DuracaoMinutos).Should().Be((50m, 30));
    }

    [Fact]
    public async Task Horarios_usam_a_duracao_do_profissional_e_qualquer_profissional_so_quem_faz_a_combinacao()
    {
        var c = await SemearAsync();
        using var cliente = ClientePara(c.Slug);
        var dia = ProximaSegundaFeira().ToString("yyyy-MM-dd");

        // Ana faz o corte em 60 min (personalizado): menos horários que com os 30 min do cadastro.
        var comServico = await Livres(cliente, $"data={dia}&duracaoMinutos=30&servicoIds={c.Corte}&profissionalId={c.Ana}");
        var semServico = await Livres(cliente, $"data={dia}&duracaoMinutos=30&profissionalId={c.Ana}");
        comServico.Should().NotBeEmpty();
        comServico.Count.Should().BeLessThan(semServico.Count);
        comServico.Select(h => h.Inicio).Should().BeSubsetOf(semServico.Select(h => h.Inicio));

        // Corte + barba: só o Beto faz os dois.
        var combinacao = await Livres(cliente, $"data={dia}&duracaoMinutos=50&servicoIds={c.Corte}&servicoIds={c.Barba}");
        combinacao.Should().NotBeEmpty().And.OnlyContain(h => h.ProfissionalId == c.Beto);
    }

    [Fact]
    public async Task Exibir_na_pagina_inicial_e_gravado_no_painel_e_nulo_na_edicao_nao_muda()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var categoria = await (await admin.PostAsJsonAsync("/painel/categorias", new { nome = "Cabelo" })).Content.ReadFromJsonAsync<Guid>();
        var criado = await admin.PostAsJsonAsync("/painel/servicos", new CriarServico(categoria, "Hidratação", 80m, 40, ExibirNaPaginaInicial: false));
        criado.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await criado.Content.ReadFromJsonAsync<Guid>();

        async Task<bool> NaPagina() => (await admin.GetFromJsonAsync<List<ServicoResumo>>("/painel/servicos"))!.Single(s => s.Id == id).ExibirNaPaginaInicial;

        (await NaPagina()).Should().BeFalse();
        (await admin.PutAsJsonAsync($"/painel/servicos/{id}", new AtualizarServico(categoria, "Hidratação", 85m, 40, false))).EnsureSuccessStatusCode();
        (await NaPagina()).Should().BeFalse();
        (await admin.PutAsJsonAsync($"/painel/servicos/{id}", new AtualizarServico(categoria, "Hidratação", 85m, 40, false, true))).EnsureSuccessStatusCode();
        (await NaPagina()).Should().BeTrue();
    }

    private sealed record Cenario(string Slug, Guid Ana, Guid Beto, Guid Corte, Guid Barba, Guid ForaDaVitrine);

    /// <summary>
    /// Ana (seg–sex, 9–12 e 13–18): corte com preço/duração próprios e um serviço fora da vitrine. Beto (seg–sex, 9–12): corte e
    /// barba. Um serviço só de um profissional inativo não entra no catálogo.
    /// </summary>
    private async Task<Cenario> SemearAsync()
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var slug = "cat-" + Guid.NewGuid().ToString("N")[..12];
        var negocio = Negocio.Criar(Slug.Criar(slug), "Catálogo de Teste", TipoNegocio.Barbearia);
        db.Negocios.Add(negocio);
        await db.SaveChangesAsync();

        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocio.Id); // Ana + "Serviço de Teste" (R$ 50, 30 min)
        var categoriaId = (await db.Servicos.IgnoreQueryFilters().SingleAsync(s => s.Id == cenario.ServicoId)).CategoriaId;

        var beto = Profissional.Criar(negocio.Id, "Beto");
        var inativo = Profissional.Criar(negocio.Id, "Caio");
        inativo.Desativar();
        db.Profissionais.AddRange(beto, inativo);
        foreach (var dia in new[] { DiaSemana.Segunda, DiaSemana.Terca, DiaSemana.Quarta, DiaSemana.Quinta, DiaSemana.Sexta })
            db.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocio.Id, beto.Id, dia, new TimeOnly(9, 0), new TimeOnly(12, 0)));

        var barba = Servico.Criar(negocio.Id, categoriaId, "Barba", 30m, 20);
        var foraDaVitrine = Servico.Criar(negocio.Id, categoriaId, "Tratamento", 90m, 45, exibirNaPaginaInicial: false);
        var semNinguem = Servico.Criar(negocio.Id, categoriaId, "Só do inativo", 10m, 15);
        db.Servicos.AddRange(barba, foraDaVitrine, semNinguem);

        db.ProfissionalServicos.AddRange(
            ProfissionalServico.Criar(negocio.Id, cenario.ProfissionalId, cenario.ServicoId, 70m, 60),
            ProfissionalServico.Criar(negocio.Id, cenario.ProfissionalId, foraDaVitrine.Id),
            ProfissionalServico.Criar(negocio.Id, beto.Id, cenario.ServicoId),
            ProfissionalServico.Criar(negocio.Id, beto.Id, barba.Id),
            ProfissionalServico.Criar(negocio.Id, inativo.Id, semNinguem.Id));
        await db.SaveChangesAsync();

        return new Cenario(slug, cenario.ProfissionalId, beto.Id, cenario.ServicoId, barba.Id, foraDaVitrine.Id);
    }

    private static async Task<List<HorarioLivrePublico>> Livres(HttpClient cliente, string consulta) =>
        (await cliente.GetFromJsonAsync<List<HorarioLivrePublico>>($"/publico/horarios-livres?{consulta}"))!;

    private static DateOnly ProximaSegundaFeira()
    {
        var dia = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        while (dia.DayOfWeek != DayOfWeek.Monday)
            dia = dia.AddDays(1);
        return dia;
    }

    private HttpClient ClientePara(string slug) => _fabrica.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri($"http://{slug}.{DominioDeTeste.Valor}/"),
    });
}
