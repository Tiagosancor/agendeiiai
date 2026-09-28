using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Aplicacao.Fidelidade;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Comissoes;

/// <summary>Comissões dos profissionais (seção 7, item 6 da seção 14).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ComissoesTestes : IAsyncLifetime
{
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public ComissoesTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Concluir_grava_a_comissao_com_o_cupom_dividido_entre_os_servicos()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await CriarProfissionalAsync(negocioId, "Ana");
        (await DefinirPercentualAsync(admin, profissionalId, 12.5m)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3 serviços de R$ 33,33 + 33,33 + 33,34 = R$ 100,00, cupom de R$ 10,00 → R$ 90,00 cobrados.
        var agendamentoId = await SemearAgendamentoAsync(negocioId, profissionalId, Local(Dia(-1), 10, 0),
            desconto: 10m, precos: [33.33m, 33.33m, 33.34m]);
        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var linhas = await LinhasAsync(agendamentoId);
        linhas.Should().OnlyContain(l => l.ComissaoProfissionalId == profissionalId && l.ComissaoPercentual == 12.5m);
        linhas.Sum(l => l.ComissaoValorBase).Should().Be(90m);
        linhas.Sum(l => l.ComissaoValor).Should().Be(11.25m); // 12,5% de R$ 90,00
        linhas.Select(l => l.ComissaoValorBase).Should().BeEquivalentTo(new decimal?[] { 30m, 30m, 30m }); // 3,33 + 3,33 + 3,34 de desconto

        var resumo = await admin.GetFromJsonAsync<ComissoesDoProfissional>(
            $"/painel/comissoes/profissionais/{profissionalId}?de={Dia(-1):yyyy-MM-dd}&ate={Dia(-1):yyyy-MM-dd}");
        resumo!.Totais.Should().Be(new TotaisComissao(11.25m, 90m, 3));
        resumo.Itens.Should().HaveCount(3);
        resumo.Itens.Should().OnlyContain(i => i.Cliente == "Cliente");
    }

    [Fact]
    public async Task Mudar_o_percentual_nao_altera_atendimentos_ja_concluidos()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await CriarProfissionalAsync(negocioId, "Bia");
        await DefinirPercentualAsync(admin, profissionalId, 40m);

        var primeiro = await SemearAgendamentoAsync(negocioId, profissionalId, Local(Dia(-2), 10, 0));
        await admin.PostAsync($"/painel/agendamentos/{primeiro}/concluir", null);

        (await DefinirPercentualAsync(admin, profissionalId, 10m)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var segundo = await SemearAgendamentoAsync(negocioId, profissionalId, Local(Dia(-1), 10, 0));
        await admin.PostAsync($"/painel/agendamentos/{segundo}/concluir", null);

        (await LinhasAsync(primeiro)).Single().ComissaoValor.Should().Be(20m); // 40% de R$ 50,00
        (await LinhasAsync(segundo)).Single().ComissaoValor.Should().Be(5m);   // 10% de R$ 50,00

        (await NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
                .Where(l => l.EntidadeId == profissionalId && l.Acao == "AlterarComissao")
                .OrderBy(l => l.CriadoEm).Select(l => l.Detalhes).ToListAsync()))
            .Should().Equal("Comissão: 0% → 40%", "Comissão: 40% → 10%");
    }

    [Fact]
    public async Task Cancelado_e_Faltou_nao_geram_comissao()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await CriarProfissionalAsync(negocioId, "Caio");
        await DefinirPercentualAsync(admin, profissionalId, 50m);

        var cancelado = await SemearAgendamentoAsync(negocioId, profissionalId, Local(Dia(-1), 9, 0));
        var faltou = await SemearAgendamentoAsync(negocioId, profissionalId, Local(Dia(-1), 10, 0));
        await admin.PostAsync($"/painel/agendamentos/{cancelado}/cancelar", null);
        await admin.PostAsync($"/painel/agendamentos/{faltou}/faltou", null);

        (await LinhasAsync(cancelado)).Single().ComissaoValor.Should().BeNull();
        (await LinhasAsync(faltou)).Single().ComissaoValor.Should().BeNull();
        var resumo = await admin.GetFromJsonAsync<ComissoesDoProfissional>(
            $"/painel/comissoes/profissionais/{profissionalId}?de={Dia(-1):yyyy-MM-dd}&ate={Dia(-1):yyyy-MM-dd}");
        resumo!.Totais.Should().Be(TotaisComissao.Zero);
    }

    [Fact]
    public async Task Reabrir_estorna_comissao_pagamento_e_selo_e_fica_na_auditoria()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await CriarProfissionalAsync(negocioId, "Duda");
        await DefinirPercentualAsync(admin, profissionalId, 30m);
        await admin.PutAsJsonAsync("/painel/fidelidade", new DefinirProgramaFidelidade(5, "Corte grátis"));

        var agendamentoId = await SemearAgendamentoAsync(negocioId, profissionalId, Local(Dia(-1), 10, 0));
        await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/concluir", null);
        (await admin.PostAsJsonAsync("/painel/pagamentos", new RegistrarPagamento(agendamentoId, 50m, "Pix")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await NoBancoAsync(db => db.SelosCliente.IgnoreQueryFilters().CountAsync(s => s.AgendamentoId == agendamentoId))).Should().Be(1);

        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/reabrir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == agendamentoId))).Status
            .Should().Be(StatusAgendamento.Agendado);
        (await LinhasAsync(agendamentoId)).Single().ComissaoValor.Should().BeNull();
        (await NoBancoAsync(db => db.Pagamentos.IgnoreQueryFilters().AnyAsync(p => p.AgendamentoId == agendamentoId))).Should().BeFalse();
        (await NoBancoAsync(db => db.SelosCliente.IgnoreQueryFilters().AnyAsync(s => s.AgendamentoId == agendamentoId))).Should().BeFalse();
        (await NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
                .SingleAsync(l => l.EntidadeId == agendamentoId && l.Acao == "ReabrirAtendimento"))).Detalhes
            .Should().Contain("R$ 15,00").And.Contain("pagamento removido: R$ 50,00");

        // Concluir de novo grava tudo outra vez; reabrir o que não está concluído é 409, não 500.
        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LinhasAsync(agendamentoId)).Single().ComissaoValor.Should().Be(15m);
        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var outro = await SemearAgendamentoAsync(negocioId, profissionalId, Local(Dia(-1), 11, 0));
        (await admin.PostAsync($"/painel/agendamentos/{outro}/reabrir", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Profissional_so_ve_as_proprias_comissoes_nem_forcando_o_identificador()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await CriarProfissionalAsync(negocioId, "Ana");
        var beto = await CriarProfissionalAsync(negocioId, "Beto");
        await DefinirPercentualAsync(admin, ana, 10m);
        await DefinirPercentualAsync(admin, beto, 50m);
        foreach (var (profissional, hora) in new[] { (ana, 9), (beto, 10) })
        {
            var id = await SemearAgendamentoAsync(negocioId, profissional, Local(Dia(-1), hora, 0));
            await admin.PostAsync($"/painel/agendamentos/{id}/concluir", null);
        }

        using var clienteAna = await LogarProfissionalAsync(negocioId, ana);
        var periodo = $"de={Dia(-1):yyyy-MM-dd}&ate={Dia(-1):yyyy-MM-dd}";

        var minhas = await clienteAna.GetFromJsonAsync<ComissoesDoProfissional>($"/painel/comissoes/minhas?{periodo}&profissionalId={beto}");
        minhas!.ProfissionalId.Should().Be(ana);
        minhas.PercentualAtual.Should().Be(10m);
        minhas.Totais.Should().Be(new TotaisComissao(5m, 50m, 1));

        (await clienteAna.GetAsync($"/painel/comissoes/profissionais/{beto}?{periodo}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await clienteAna.GetAsync($"/painel/comissoes/resumo?{periodo}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await clienteAna.GetAsync($"/painel/profissionais/{beto}/comissao")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DefinirPercentualAsync(clienteAna, ana, 90m)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Usuario_sem_permissao_nao_altera_nem_ve_o_percentual()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await CriarProfissionalAsync(negocioId, "Ana");
        using var recepcao = await LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista);

        (await DefinirPercentualAsync(recepcao, profissionalId, 20m)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.GetAsync($"/painel/profissionais/{profissionalId}/comissao")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await recepcao.GetAsync($"/painel/comissoes/resumo?de={Dia(-1):yyyy-MM-dd}&ate={Dia(0):yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Sem vínculo com profissional, "Minhas comissões" responde vazio, sem erro.
        var minhas = await recepcao.GetFromJsonAsync<ComissoesDoProfissional>($"/painel/comissoes/minhas?de={Dia(-1):yyyy-MM-dd}&ate={Dia(0):yyyy-MM-dd}");
        minhas!.ProfissionalId.Should().BeNull();
        minhas.Totais.Should().Be(TotaisComissao.Zero);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(12.345)]
    public async Task Percentual_invalido_e_recusado(double percentual)
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await CriarProfissionalAsync(negocioId, "Ana");

        (await DefinirPercentualAsync(admin, profissionalId, (decimal)percentual)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetFromJsonAsync<PercentualComissaoResposta>($"/painel/profissionais/{profissionalId}/comissao"))!.Percentual.Should().Be(0m);
    }

    [Fact]
    public async Task Filtro_usa_o_dia_no_fuso_do_negocio()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await CriarProfissionalAsync(negocioId, "Ana");
        await DefinirPercentualAsync(admin, profissionalId, 10m);

        // 23:30 de um dia (02:30 UTC do dia seguinte) e 00:10 do dia seguinte (03:10 UTC).
        var dia = Dia(-3);
        foreach (var inicio in new[] { Local(dia, 23, 30), Local(dia.AddDays(1), 0, 10) })
        {
            var id = await SemearAgendamentoAsync(negocioId, profissionalId, inicio);
            await admin.PostAsync($"/painel/agendamentos/{id}/concluir", null);
        }

        async Task<int> QuantidadeAsync(DateOnly de, DateOnly ate) =>
            (await admin.GetFromJsonAsync<ComissoesDoProfissional>(
                $"/painel/comissoes/profissionais/{profissionalId}?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}"))!.Totais.QuantidadeServicos;

        (await QuantidadeAsync(dia, dia)).Should().Be(1);
        (await QuantidadeAsync(dia.AddDays(1), dia.AddDays(1))).Should().Be(1);
        (await QuantidadeAsync(dia, dia.AddDays(1))).Should().Be(2);
    }

    [Fact]
    public async Task Periodo_vazio_devolve_zeros_e_o_resumo_lista_todos()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await CriarProfissionalAsync(negocioId, "Ana");

        var periodo = $"de={Dia(-30):yyyy-MM-dd}&ate={Dia(-20):yyyy-MM-dd}";
        var detalhe = await admin.GetFromJsonAsync<ComissoesDoProfissional>($"/painel/comissoes/profissionais/{profissionalId}?{periodo}");
        detalhe!.Totais.Should().Be(TotaisComissao.Zero);
        detalhe.Itens.Should().BeEmpty();

        var resumo = await admin.GetFromJsonAsync<List<ResumoComissaoProfissional>>($"/painel/comissoes/resumo?{periodo}");
        resumo!.Should().ContainSingle(r => r.ProfissionalId == profissionalId).Which.Totais.Should().Be(TotaisComissao.Zero);

        (await admin.GetAsync($"/painel/comissoes/resumo?de={Dia(0):yyyy-MM-dd}&ate={Dia(-1):yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Resumo_do_administrador_soma_por_profissional()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var ana = await CriarProfissionalAsync(negocioId, "Ana");
        var beto = await CriarProfissionalAsync(negocioId, "Beto");
        await DefinirPercentualAsync(admin, ana, 10m);
        await DefinirPercentualAsync(admin, beto, 20m);
        foreach (var (profissional, hora) in new[] { (ana, 9), (ana, 10), (beto, 11) })
        {
            var id = await SemearAgendamentoAsync(negocioId, profissional, Local(Dia(-1), hora, 0));
            await admin.PostAsync($"/painel/agendamentos/{id}/concluir", null);
        }

        var resumo = await admin.GetFromJsonAsync<List<ResumoComissaoProfissional>>(
            $"/painel/comissoes/resumo?de={Dia(-1):yyyy-MM-dd}&ate={Dia(-1):yyyy-MM-dd}");

        resumo!.Single(r => r.ProfissionalId == ana).Totais.Should().Be(new TotaisComissao(10m, 100m, 2));
        resumo!.Single(r => r.ProfissionalId == beto).Totais.Should().Be(new TotaisComissao(10m, 50m, 1));
    }

    // ---------------------------------------------------------------- apoio

    private static DateOnly Dia(int deslocamento) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso).DateTime).AddDays(deslocamento);

    private static DateTimeOffset Local(DateOnly dia, int hora, int minuto)
    {
        var local = dia.ToDateTime(new TimeOnly(hora, minuto), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Fuso.GetUtcOffset(local)).ToUniversalTime();
    }

    private static Task<HttpResponseMessage> DefinirPercentualAsync(HttpClient cliente, Guid profissionalId, decimal percentual) =>
        cliente.PutAsJsonAsync($"/painel/profissionais/{profissionalId}/comissao", new PercentualComissaoRequisicao(percentual));

    private async Task<Guid> CriarProfissionalAsync(Guid negocioId, string nome) =>
        await NoBancoAsync(async db =>
        {
            var profissional = Profissional.Criar(negocioId, nome);
            db.Profissionais.Add(profissional);
            await db.SaveChangesAsync();
            return profissional.Id;
        });

    /// <summary>Direto no banco (sem expediente): os testes precisam de horários passados e de madrugada.</summary>
    private async Task<Guid> SemearAgendamentoAsync(
        Guid negocioId, Guid profissionalId, DateTimeOffset inicio, decimal desconto = 0m, decimal[]? precos = null)
    {
        return await NoBancoAsync(async db =>
        {
            var cliente = Plataforma.Dominio.Clientes.Cliente.Criar(negocioId, "Cliente Sobrenome",
                Plataforma.Dominio.Comum.TelefoneE164.Criar($"+55719{Random.Shared.Next(10000000, 99999999)}"));
            db.Clientes.Add(cliente);

            var itens = (precos ?? [50m]).Select((preco, i) => new ItemServicoAgendamento(Guid.NewGuid(), $"Serviço {i + 1}", preco, 20)).ToList();
            var agendamento = Agendamento.CriarConfirmado(negocioId, profissionalId, cliente.Id, inicio, itens);
            if (desconto > 0)
                typeof(Agendamento).GetProperty(nameof(Agendamento.DescontoAplicado))!.SetValue(agendamento, desconto);

            db.Agendamentos.Add(agendamento);
            await db.SaveChangesAsync();
            return agendamento.Id;
        });
    }

    private Task<List<AgendamentoServico>> LinhasAsync(Guid agendamentoId) =>
        NoBancoAsync(db => db.Set<AgendamentoServico>().IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.AgendamentoId == agendamentoId).OrderBy(s => s.Nome).ToListAsync());

    private Task<HttpClient> LogarProfissionalAsync(Guid negocioId, Guid profissionalId) =>
        LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, profissionalId);

    private async Task<HttpClient> LogarNovoUsuarioAsync(Guid negocioId, Perfil perfil, Guid? profissionalId = null)
    {
        var email = $"u-{Guid.NewGuid():N}@teste.com";
        await NoBancoAsync(async db =>
        {
            var hasher = _fabrica.Services.GetRequiredService<ISenhaHasher>();
            var usuario = Usuario.Criar(negocioId, "Usuário", email, perfil, hasher.Hash(SemeadorDeUsuarios.SenhaPadrao));
            if (profissionalId is { } id)
                usuario.VincularProfissional(id);
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            return 0;
        });

        using var anonimo = _fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SemeadorDeUsuarios.SenhaPadrao));
        var token = (await login.Content.ReadFromJsonAsync<RespostaLogin>())!.AccessToken;

        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    private async Task<T> NoBancoAsync<T>(Func<PlataformaDbContext, Task<T>> acao)
    {
        using var escopo = _fabrica.Services.CreateScope();
        return await acao(escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>());
    }
}
