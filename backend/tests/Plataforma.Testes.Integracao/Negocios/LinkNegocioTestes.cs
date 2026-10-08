using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Cadastro;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Negocios;

/// <summary>Trocar o link do negócio depois do cadastro (item 13 da seção 14, seção 5).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class LinkNegocioTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public LinkNegocioTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Trocar_para_um_link_livre_muda_a_pagina_os_emails_e_os_primeiros_passos()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var antigo = await SlugAtualAsync(admin);
        var novo = Novo();

        var resposta = await TrocarAsync(admin, novo);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var link = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        link.GetProperty("slug").GetString().Should().Be(novo);
        link.GetProperty("url").GetString().Should().Contain($"//{novo}.");
        link.GetProperty("proximaTrocaEm").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));

        (await admin.GetFromJsonAsync<PerfilNegocio>("/painel/negocio"))!.Slug.Should().Be(novo);
        (await admin.GetFromJsonAsync<PrimeirosPassos>("/painel/primeiros-passos"))!.LinkAgendamento.Should().Contain($"//{novo}.");

        using var anonimo = _fabrica.CreateClient();
        (await anonimo.GetAsync($"/publico/negocios-por-slug/{novo}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonimo.GetAsync($"/publico/negocios-por-slug/{antigo}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // E-mail enviado depois da troca (confirmação de um encaixe com aceite) já leva o link novo.
        var (profissionalId, servicoId) = await CenarioAsync(negocioId);
        var email = $"cliente-{Guid.NewGuid():N}@teste.com";
        var clienteId = await (await admin.PostAsJsonAsync("/painel/clientes",
            new CriarCliente("Cliente do Link", $"719{Random.Shared.Next(10000000, 99999999)}", email))).Content.ReadFromJsonAsync<Guid>();
        (await admin.PostAsJsonAsync("/painel/encaixes", new LancarEncaixe(
            profissionalId, clienteId, null, [servicoId], SemeadorDeComissoes.Local(SemeadorDeComissoes.Dia(1), 12, 0), false, true))).StatusCode.Should().Be(HttpStatusCode.Created);

        var enviado = _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Single(e => e.Destinatario == email);
        enviado.CorpoHtml.Should().Contain($"//{novo}.").And.NotContain($"//{antigo}.");
    }

    [Fact]
    public async Task Link_reservado_invalido_ou_de_outro_negocio_e_recusado()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (outro, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var doOutro = await SlugAtualAsync(outro);

        (await TrocarAsync(admin, "admin")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TrocarAsync(admin, "Com Espaço")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TrocarAsync(admin, "-hifen")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TrocarAsync(admin, await SlugAtualAsync(admin))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var emUso = await TrocarAsync(admin, doOutro);
        emUso.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await emUso.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("slug_em_uso");

        (await Disponibilidade(admin, doOutro)).Disponivel.Should().BeFalse();
        (await Disponibilidade(admin, "www")).Motivo.Should().Contain("reservado");
        (await Disponibilidade(admin, await SlugAtualAsync(admin))).Motivo.Should().Contain("link atual");
        (await Disponibilidade(admin, Novo())).Disponivel.Should().BeTrue();

        // Nada mudou.
        (await admin.GetFromJsonAsync<JsonElement>("/painel/negocio/link")).GetProperty("ultimaTrocaEm").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Link_antigo_redireciona_por_90_dias_e_depois_deixa_de_redirecionar()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var antigo = await SlugAtualAsync(admin);
        var novo = Novo();
        (await TrocarAsync(admin, novo)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var anonimo = _fabrica.CreateClient();

        var redirecionamento = await anonimo.GetFromJsonAsync<JsonElement>($"/publico/slugs-anteriores/{antigo}");
        redirecionamento.GetProperty("slugAtual").GetString().Should().Be(novo);
        (await anonimo.GetAsync($"/publico/slugs-anteriores/{novo}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var registro = await _fabrica.NoBancoAsync(db => db.SlugsAnteriores.SingleAsync(s => s.NegocioId == negocioId));
        registro.RedirecionaAte.Should().BeCloseTo(registro.TrocadoEm.AddDays(90), TimeSpan.FromSeconds(1));

        await Envelhecer(negocioId, dias: 91);

        (await anonimo.GetAsync($"/publico/slugs-anteriores/{antigo}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Link_antigo_fica_ocupado_para_outros_negocios_ate_o_prazo_acabar()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (outro, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var antigo = await SlugAtualAsync(admin);
        (await TrocarAsync(admin, Novo())).StatusCode.Should().Be(HttpStatusCode.OK);
        using var anonimo = _fabrica.CreateClient();

        var tentativa = await TrocarAsync(outro, antigo);
        tentativa.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Disponibilidade(outro, antigo)).Disponivel.Should().BeFalse();
        (await anonimo.GetFromJsonAsync<DisponibilidadeSlug>($"/cadastro/slugs/{antigo}"))!.Disponivel.Should().BeFalse();

        await Envelhecer(negocioId, dias: 91);

        (await anonimo.GetFromJsonAsync<DisponibilidadeSlug>($"/cadastro/slugs/{antigo}"))!.Disponivel.Should().BeTrue();
        (await TrocarAsync(outro, antigo)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Segunda_troca_antes_de_30_dias_e_recusada_com_a_data_liberada()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var original = await SlugAtualAsync(admin);
        (await TrocarAsync(admin, Novo())).StatusCode.Should().Be(HttpStatusCode.OK);

        var segunda = await TrocarAsync(admin, Novo());

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problema = await segunda.Content.ReadFromJsonAsync<JsonElement>();
        problema.GetProperty("codigo").GetString().Should().Be("troca_recente");
        var podeTrocarEm = problema.GetProperty("podeTrocarEm").GetDateTimeOffset();
        podeTrocarEm.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        problema.GetProperty("title").GetString().Should().Contain(podeTrocarEm.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy"));

        // Passados os 30 dias, pode — inclusive voltar para o link original, que deixa de redirecionar.
        await Envelhecer(negocioId, dias: 31);
        (await TrocarAsync(admin, original)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.GetAsync($"/publico/slugs-anteriores/{original}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonimo.GetAsync($"/publico/negocios-por-slug/{original}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task So_o_Administrador_troca_o_link()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista,
            permissoesExtras: [Permissao.GerenciarConfiguracoesDoNegocio]);

        var link = await recepcao.GetFromJsonAsync<JsonElement>("/painel/negocio/link");
        link.GetProperty("podeEditar").GetBoolean().Should().BeFalse();
        (await TrocarAsync(recepcao, Novo())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.GetAsync($"/painel/negocio/link/disponibilidade?slug={Novo()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var profissional = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional);
        (await profissional.GetAsync("/painel/negocio/link")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Toda_troca_entra_na_auditoria_com_o_antes_o_depois_e_o_autor()
    {
        var (admin, negocioId, usuarioId, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var antigo = await SlugAtualAsync(admin);
        var novo = Novo();

        (await TrocarAsync(admin, novo)).StatusCode.Should().Be(HttpStatusCode.OK);

        var registro = await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .SingleAsync(l => l.NegocioId == negocioId && l.Acao == GerenciadorLinkNegocio.AcaoAuditoria));
        registro.AutorUsuarioId.Should().Be(usuarioId);
        registro.EntidadeId.Should().Be(negocioId);
        registro.Detalhes.Should().Contain(antigo).And.Contain(novo);
    }

    // ---------------------------------------------------------------- apoio

    private static string Novo() => "link-" + Guid.NewGuid().ToString("N")[..12];

    private static Task<HttpResponseMessage> TrocarAsync(HttpClient cliente, string slug) =>
        cliente.PutAsJsonAsync("/painel/negocio/link", new { slug });

    private static async Task<DisponibilidadeSlug> Disponibilidade(HttpClient cliente, string slug) =>
        (await cliente.GetFromJsonAsync<DisponibilidadeSlug>($"/painel/negocio/link/disponibilidade?slug={Uri.EscapeDataString(slug)}"))!;

    private static async Task<string> SlugAtualAsync(HttpClient cliente) =>
        (await cliente.GetFromJsonAsync<JsonElement>("/painel/negocio/link")).GetProperty("slug").GetString()!;

    /// <summary>Joga a última troca (e o prazo do link antigo) para trás, como se tivessem passado tantos dias.</summary>
    private Task Envelhecer(Guid negocioId, int dias) =>
        _fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE negocios SET slug_alterado_em = slug_alterado_em - make_interval(days => {dias}) WHERE id = {negocioId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE slugs_anteriores SET trocado_em = trocado_em - make_interval(days => {dias}), redireciona_ate = redireciona_ate - make_interval(days => {dias}) WHERE negocio_id = {negocioId}");
            return 0;
        });

    private Task<(Guid ProfissionalId, Guid ServicoId)> CenarioAsync(Guid negocioId) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var profissional = Profissional.Criar(negocioId, "Profissional do Link");
            db.Profissionais.Add(profissional);
            foreach (var dia in Enum.GetValues<DiaSemana>())
                db.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocioId, profissional.Id, dia, new TimeOnly(0, 0), new TimeOnly(23, 59)));
            var categoria = Categoria.Criar(negocioId, "Cabelo");
            db.Categorias.Add(categoria);
            var servico = Servico.Criar(negocioId, categoria.Id, "Corte", 40m, 30);
            db.Servicos.Add(servico);
            await db.SaveChangesAsync();
            return (profissional.Id, servico.Id);
        });
}
