using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>Gestão pública do agendamento por token (seção 6.3): detalhe com ações permitidas, horários para remarcar, remarcar e cancelar.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class GestaoPublicaAgendamentoTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public GestaoPublicaAgendamentoTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    // ---------------------------------------------------------------- detalhe

    [Fact]
    public async Task Detalhe_traz_contexto_acoes_liberadas_e_mantem_os_campos_antigos()
    {
        var c = await ArranjarAsync();
        var (id, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));

        var resposta = await c.Publico.GetAsync($"/publico/meus-agendamentos/{token}");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resposta.Content.ReadFromJsonAsync<JsonElement>();

        // Campos que o frontend em produção já lê.
        json.GetProperty("id").GetGuid().Should().Be(id);
        json.GetProperty("nomeNegocio").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("servicos").EnumerateArray().Select(s => s.GetString()).Should().Equal("Serviço 1");
        json.GetProperty("total").GetDecimal().Should().Be(50m);
        json.GetProperty("status").GetString().Should().Be("Agendado");
        json.GetProperty("inicio").GetDateTimeOffset().Should().Be(Local(c.Segunda, 10, 0));

        // Contexto novo.
        json.GetProperty("fuso").GetString().Should().Be("America/Sao_Paulo");
        json.GetProperty("duracaoMinutos").GetInt32().Should().Be(20);
        json.GetProperty("profissional").GetProperty("id").GetGuid().Should().Be(c.Cenario.ProfissionalId);
        json.GetProperty("profissional").GetProperty("nome").GetString().Should().Be("Profissional de Teste");
        var servico = json.GetProperty("servicosDetalhe").EnumerateArray().Single();
        servico.GetProperty("servicoId").GetGuid().Should().Be(c.Cenario.ServicoId);
        servico.GetProperty("duracaoMinutos").GetInt32().Should().Be(20);
        json.GetProperty("regras").GetProperty("antecedenciaMinimaHoras").GetInt32().Should().Be(2);
        json.GetProperty("regras").GetProperty("limiteParaAlterarEm").GetDateTimeOffset().Should().Be(Local(c.Segunda, 8, 0));

        json.GetProperty("acoes").GetProperty("cancelar").GetProperty("permitido").GetBoolean().Should().BeTrue();
        json.GetProperty("acoes").GetProperty("remarcar").GetProperty("permitido").GetBoolean().Should().BeTrue();
        json.GetProperty("acoes").GetProperty("cancelar").TryGetProperty("codigoMotivo", out var semMotivo).Should().BeTrue();
        semMotivo.ValueKind.Should().Be(JsonValueKind.Null);

        // Nada administrativo vaza.
        var texto = json.GetRawText();
        texto.Should().NotContain("cliente").And.NotContain("comissao").And.NotContain("forcado").And.NotContain("negocioId");
    }

    [Fact]
    public async Task Token_invalido_de_outro_tipo_ou_de_outro_negocio_e_recusado_sem_vazar_nada()
    {
        var c = await ArranjarAsync();
        var (id, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        var tokens = _fabrica.Services.GetRequiredService<IServicoTokenPublico>();

        var outro = await CriarOutroNegocioAsync();
        using var publicoOutro = ClientePorSlug(outro.Slug);

        var invalidos = new[]
        {
            (c.Publico, "lixo.invalido.token"),
            (c.Publico, token[..^3] + "abc"),
            (c.Publico, tokens.GerarTokenVerificacao(c.NegocioId, Plataforma.Dominio.Comum.TelefoneE164.Criar("+5571999990000"))),
            (publicoOutro, token),
        };

        foreach (var (cliente, tokenInvalido) in invalidos)
        {
            foreach (var url in new[] { "", "/horarios-livres?data=2030-01-07", "/ics" })
            {
                var resposta = await cliente.GetAsync($"/publico/meus-agendamentos/{tokenInvalido}{url}");
                resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                var json = await resposta.Content.ReadFromJsonAsync<JsonElement>();
                json.GetProperty("codigo").GetString().Should().Be("link_invalido");
                json.GetRawText().Should().NotContain(id.ToString());
            }
        }

        (await publicoOutro.PostAsync($"/publico/meus-agendamentos/{token}/cancelar", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Status(c, id)).Should().Be(StatusAgendamento.Agendado);
    }

    [Fact]
    public async Task Token_de_agendamento_inexistente_devolve_404_com_codigo()
    {
        var c = await ArranjarAsync();
        var token = _fabrica.Services.GetRequiredService<IServicoTokenPublico>().GerarTokenAgendamento(c.NegocioId, Guid.NewGuid());

        var resposta = await c.Publico.GetAsync($"/publico/meus-agendamentos/{token}");

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("nao_encontrado");
    }

    [Theory]
    [InlineData("Cancelado")]
    [InlineData("Concluido")]
    public async Task Status_final_bloqueia_cancelar_e_remarcar_com_codigo_estavel(string status)
    {
        var c = await ArranjarAsync();
        var (id, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        await MudarStatusAsync(id, status);

        var acoes = await AcoesAsync(c, token);

        foreach (var acao in new[] { acoes.GetProperty("cancelar"), acoes.GetProperty("remarcar") })
        {
            acao.GetProperty("permitido").GetBoolean().Should().BeFalse();
            acao.GetProperty("codigoMotivo").GetString().Should().Be("status_nao_permite");
            acao.GetProperty("motivo").GetString().Should().NotBeNullOrEmpty();
        }

        // O POST revalida: 409 com o mesmo código (antes era 500 para estes casos).
        var cancelar = await c.Publico.PostAsync($"/publico/meus-agendamentos/{token}/cancelar", null);
        cancelar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await cancelar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("status_nao_permite");

        var remarcar = await c.Publico.PostAsJsonAsync($"/publico/meus-agendamentos/{token}/remarcar", new { novoInicio = Local(c.Segunda, 11, 0) });
        remarcar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await remarcar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("status_nao_permite");

        (await Status(c, id)).ToString().Should().Be(status);
    }

    [Fact]
    public async Task Em_atendimento_nao_pode_ser_remarcado()
    {
        var c = await ArranjarAsync();
        var (id, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        await MudarStatusAsync(id, "EmAtendimento");

        var acoes = await AcoesAsync(c, token);

        acoes.GetProperty("remarcar").GetProperty("codigoMotivo").GetString().Should().Be("status_nao_permite");
    }

    [Fact]
    public async Task Antecedencia_e_horario_passado_aparecem_nas_acoes_e_o_post_concorda()
    {
        var c = await ArranjarAsync();
        var emBreve = await SemearAsync(c, DateTimeOffset.UtcNow.AddHours(1));
        var passado = await SemearAsync(c, DateTimeOffset.UtcNow.AddHours(-3));
        var folgado = await SemearAsync(c, DateTimeOffset.UtcNow.AddHours(3));

        var casos = new[]
        {
            (emBreve.Token, "antecedencia_minima"),
            (passado.Token, "agendamento_passado"),
            (folgado.Token, (string?)null),
        };

        foreach (var (token, codigoEsperado) in casos)
        {
            var acoes = await AcoesAsync(c, token);
            var cancelar = await c.Publico.PostAsync($"/publico/meus-agendamentos/{token}/cancelar", null);

            foreach (var nome in new[] { "cancelar", "remarcar" })
            {
                acoes.GetProperty(nome).GetProperty("permitido").GetBoolean().Should().Be(codigoEsperado is null);
                if (codigoEsperado is not null)
                    acoes.GetProperty(nome).GetProperty("codigoMotivo").GetString().Should().Be(codigoEsperado);
            }

            if (codigoEsperado is null)
                cancelar.StatusCode.Should().Be(HttpStatusCode.NoContent);
            else
            {
                cancelar.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                (await cancelar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be(codigoEsperado);
            }
        }

        (await Status(c, emBreve.Id)).Should().Be(StatusAgendamento.Agendado);
        (await Status(c, passado.Id)).Should().Be(StatusAgendamento.Agendado);
        (await Status(c, folgado.Id)).Should().Be(StatusAgendamento.Cancelado);
    }

    [Fact]
    public async Task Respostas_por_token_nunca_ficam_em_cache()
    {
        var c = await ArranjarAsync();
        var (_, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));

        var respostas = new[]
        {
            await c.Publico.GetAsync($"/publico/meus-agendamentos/{token}"),
            await c.Publico.GetAsync($"/publico/meus-agendamentos/{token}/horarios-livres?data={c.Segunda:yyyy-MM-dd}"),
            await c.Publico.GetAsync($"/publico/meus-agendamentos/{token}/ics"),
            await c.Publico.GetAsync("/publico/meus-agendamentos/token-ruim"),
        };

        foreach (var resposta in respostas)
            resposta.Headers.CacheControl!.NoStore.Should().BeTrue();

        respostas[2].StatusCode.Should().Be(HttpStatusCode.OK);
        respostas[2].Content.Headers.ContentType!.MediaType.Should().Be("text/calendar");
    }

    // ---------------------------------------------------------------- disponibilidade

    [Fact]
    public async Task Horarios_para_remarcar_excluem_so_o_proprio_agendamento_e_respeitam_as_regras()
    {
        var c = await ArranjarAsync();
        var (_, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        await SemearAsync(c, Local(c.Segunda, 11, 0));
        await _fabrica.NoBancoAsync(async db =>
        {
            db.BloqueiosAgenda.Add(BloqueioAgenda.Criar(c.NegocioId, c.Cenario.ProfissionalId, Local(c.Segunda, 14, 0), Local(c.Segunda, 15, 0), "Dentista"));
            return await db.SaveChangesAsync();
        });

        var resposta = await c.Publico.GetAsync($"/publico/meus-agendamentos/{token}/horarios-livres?data={c.Segunda:yyyy-MM-dd}");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        var horarios = json.GetProperty("horarios").EnumerateArray().Select(h => h.GetDateTimeOffset()).ToList();

        json.GetProperty("fuso").GetString().Should().Be("America/Sao_Paulo");
        json.GetProperty("data").GetString().Should().Be($"{c.Segunda:yyyy-MM-dd}");
        json.GetProperty("duracaoMinutos").GetInt32().Should().Be(20);
        json.GetProperty("profissionalId").GetGuid().Should().Be(c.Cenario.ProfissionalId);
        json.GetProperty("horarios").EnumerateArray().Select(h => h.GetString()).Should().OnlyContain(h => h!.EndsWith("+00:00"));

        // O próprio horário (10:00 e o que o cruza) está livre para ele...
        horarios.Should().Contain([Local(c.Segunda, 10, 0), Local(c.Segunda, 10, 15), Local(c.Segunda, 9, 45)]);
        // ...mas o outro agendamento continua ocupando 11:00 (e o que cruza com ele).
        horarios.Should().NotContain([Local(c.Segunda, 11, 0), Local(c.Segunda, 10, 45), Local(c.Segunda, 11, 15)]);
        horarios.Should().Contain(Local(c.Segunda, 11, 30));
        // Expediente, almoço e bloqueio.
        horarios.Should().NotContain([Local(c.Segunda, 8, 45), Local(c.Segunda, 12, 0), Local(c.Segunda, 11, 45), Local(c.Segunda, 14, 0), Local(c.Segunda, 14, 45), Local(c.Segunda, 17, 45)]);
        horarios.Should().Contain([Local(c.Segunda, 15, 0), Local(c.Segunda, 13, 0), Local(c.Segunda, 17, 30)]);
    }

    [Fact]
    public async Task Mesmo_motor_do_horarios_livres_publico_a_diferenca_e_so_a_ocupacao_do_proprio_agendamento()
    {
        var c = await ArranjarAsync();
        var (_, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        await SemearAsync(c, Local(c.Segunda, 15, 0));

        var remarcacao = await HorariosAsync(c, token, c.Segunda);
        var publico = (await c.Publico.GetFromJsonAsync<List<HorarioPublico>>(
            $"/publico/horarios-livres?data={c.Segunda:yyyy-MM-dd}&duracaoMinutos=20&servicoIds={c.Cenario.ServicoId}&profissionalId={c.Cenario.ProfissionalId}"))!
            .Select(h => h.Inicio).ToList();

        publico.Should().BeSubsetOf(remarcacao);
        var soNaRemarcacao = remarcacao.Except(publico).ToList();
        soNaRemarcacao.Should().NotBeEmpty();
        soNaRemarcacao.Should().OnlyContain(h => h > Local(c.Segunda, 9, 40) && h < Local(c.Segunda, 10, 20));
    }

    [Fact]
    public async Task Contexto_vem_so_do_token_parametros_extras_e_outro_profissional_sao_ignorados()
    {
        var c = await ArranjarAsync();
        var (_, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        var outroProfissional = await _fabrica.CriarProfissionalAsync(c.NegocioId, "Outro Profissional");
        await _fabrica.SemearAgendamentoAsync(c.NegocioId, outroProfissional, Local(c.Segunda, 15, 0), servicoId: c.Cenario.ServicoId);

        var normal = await HorariosAsync(c, token, c.Segunda);
        var comTentativa = (await c.Publico.GetFromJsonAsync<JsonElement>(
            $"/publico/meus-agendamentos/{token}/horarios-livres?data={c.Segunda:yyyy-MM-dd}&profissionalId={outroProfissional}&duracaoMinutos=240&servicoIds={Guid.NewGuid()}"))
            .GetProperty("horarios").EnumerateArray().Select(h => h.GetDateTimeOffset()).ToList();

        comTentativa.Should().Equal(normal);
        // O agendamento de OUTRO profissional não ocupa a agenda deste.
        normal.Should().Contain(Local(c.Segunda, 15, 0));
    }

    [Fact]
    public async Task Duracao_vem_dos_servicos_do_proprio_agendamento()
    {
        var c = await ArranjarAsync();
        // Dois serviços de 20 min = 40 min.
        var (_, token) = await SemearAsync(c, Local(c.Segunda, 10, 0), precos: [50m, 30m]);
        await SemearAsync(c, Local(c.Segunda, 11, 0));

        var json = await c.Publico.GetFromJsonAsync<JsonElement>($"/publico/meus-agendamentos/{token}/horarios-livres?data={c.Segunda:yyyy-MM-dd}");
        var horarios = json.GetProperty("horarios").EnumerateArray().Select(h => h.GetDateTimeOffset()).ToList();

        json.GetProperty("duracaoMinutos").GetInt32().Should().Be(40);
        horarios.Should().Contain(Local(c.Segunda, 10, 15)).And.NotContain(Local(c.Segunda, 10, 30));
    }

    [Fact]
    public async Task Horarios_exigem_data_e_respeitam_a_decisao_de_remarcar()
    {
        var c = await ArranjarAsync();
        var (_, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        var emBreve = await SemearAsync(c, DateTimeOffset.UtcNow.AddHours(1));
        var concluido = await SemearAsync(c, Local(c.Segunda, 16, 0));
        await MudarStatusAsync(concluido.Id, "Concluido");

        var semData = await c.Publico.GetAsync($"/publico/meus-agendamentos/{token}/horarios-livres");
        semData.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await semData.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("data_invalida");

        var antecedencia = await c.Publico.GetAsync($"/publico/meus-agendamentos/{emBreve.Token}/horarios-livres?data={c.Segunda:yyyy-MM-dd}");
        antecedencia.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await antecedencia.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("antecedencia_minima");

        var status = await c.Publico.GetAsync($"/publico/meus-agendamentos/{concluido.Token}/horarios-livres?data={c.Segunda:yyyy-MM-dd}");
        status.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("status_nao_permite");
    }

    [Fact]
    public async Task Dia_de_folga_nao_tem_horarios()
    {
        var c = await ArranjarAsync();
        var (_, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));

        (await HorariosAsync(c, token, c.Segunda.AddDays(-1))).Should().BeEmpty(); // domingo
    }

    // ---------------------------------------------------------------- remarcação

    [Fact]
    public async Task Remarcar_para_horario_listado_funciona_inclusive_por_cima_do_proprio_horario()
    {
        var c = await ArranjarAsync();
        var (id, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        (await HorariosAsync(c, token, c.Segunda)).Should().Contain(Local(c.Segunda, 10, 15));

        // 10:15 cruza o 10:00–10:20 atual do próprio agendamento — só é possível porque ele não conta como conflito.
        // O offset local (-03:00) e o instante UTC são o mesmo horário.
        var resposta = await c.Publico.PostAsJsonAsync($"/publico/meus-agendamentos/{token}/remarcar",
            new { novoInicio = $"{c.Segunda:yyyy-MM-dd}T10:15:00-03:00" });

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var salvo = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == id));
        salvo.Inicio.Should().Be(Local(c.Segunda, 10, 15));
        salvo.Fim.Should().Be(Local(c.Segunda, 10, 35));
    }

    [Fact]
    public async Task Remarcar_com_prefer_devolve_o_detalhe_canonico_atualizado()
    {
        var c = await ArranjarAsync();
        var (_, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/publico/meus-agendamentos/{token}/remarcar")
        {
            Content = JsonContent.Create(new { novoInicio = Local(c.Segunda, 14, 0) }),
        };
        requisicao.Headers.Add("Prefer", "return=representation");
        var resposta = await c.Publico.SendAsync(requisicao);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        resposta.Headers.GetValues("Preference-Applied").Should().Contain("return=representation");
        var json = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("inicio").GetDateTimeOffset().Should().Be(Local(c.Segunda, 14, 0));
        json.GetProperty("fim").GetDateTimeOffset().Should().Be(Local(c.Segunda, 14, 20));
        json.GetProperty("acoes").GetProperty("remarcar").GetProperty("permitido").GetBoolean().Should().BeTrue();
        resposta.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Horario_que_ficou_indisponivel_depois_da_consulta_da_409_com_codigo_e_proximos_horarios()
    {
        var c = await ArranjarAsync();
        var (id, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        (await HorariosAsync(c, token, c.Segunda)).Should().Contain(Local(c.Segunda, 10, 30));

        // Outra pessoa ocupa o horário depois que a tela listou.
        await SemearAsync(c, Local(c.Segunda, 10, 30));

        var resposta = await c.Publico.PostAsJsonAsync($"/publico/meus-agendamentos/{token}/remarcar", new { novoInicio = Local(c.Segunda, 10, 30) });

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("codigo").GetString().Should().Be("horario_indisponivel");
        json.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        var proximos = json.GetProperty("proximosHorariosLivres").EnumerateArray().Select(h => h.GetDateTimeOffset()).ToList();
        proximos.Should().NotBeEmpty().And.NotContain(Local(c.Segunda, 10, 30));
        proximos.Should().OnlyContain(h => h > DateTimeOffset.UtcNow);

        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == id))).Inicio
            .Should().Be(Local(c.Segunda, 10, 0));
    }

    [Fact]
    public async Task Regras_de_expediente_e_bloqueio_continuam_valendo_no_post()
    {
        var c = await ArranjarAsync();
        var (id, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        await _fabrica.NoBancoAsync(async db =>
        {
            db.BloqueiosAgenda.Add(BloqueioAgenda.Criar(c.NegocioId, c.Cenario.ProfissionalId, Local(c.Segunda, 14, 0), Local(c.Segunda, 15, 0), "Dentista"));
            return await db.SaveChangesAsync();
        });

        foreach (var horario in new[] { Local(c.Segunda, 19, 0), Local(c.Segunda, 12, 0), Local(c.Segunda, 14, 15) })
        {
            var resposta = await c.Publico.PostAsJsonAsync($"/publico/meus-agendamentos/{token}/remarcar", new { novoInicio = horario });
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("horario_invalido");
        }

        (await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == id))).Inicio
            .Should().Be(Local(c.Segunda, 10, 0));
    }

    // ---------------------------------------------------------------- cancelamento

    [Fact]
    public async Task Cancelar_permitido_grava_e_o_detalhe_passa_a_bloquear_as_acoes()
    {
        var c = await ArranjarAsync();
        var (id, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));

        var resposta = await c.Publico.PostAsync($"/publico/meus-agendamentos/{token}/cancelar", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Status(c, id)).Should().Be(StatusAgendamento.Cancelado);

        var acoes = await AcoesAsync(c, token);
        acoes.GetProperty("cancelar").GetProperty("codigoMotivo").GetString().Should().Be("status_nao_permite");
        acoes.GetProperty("remarcar").GetProperty("permitido").GetBoolean().Should().BeFalse();

        // Cancelar de novo é recusado com 409 estável, não 500.
        var denovo = await c.Publico.PostAsync($"/publico/meus-agendamentos/{token}/cancelar", null);
        denovo.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await denovo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().Should().Be("status_nao_permite");
    }

    [Fact]
    public async Task Cancelar_com_prefer_devolve_o_detalhe_ja_cancelado()
    {
        var c = await ArranjarAsync();
        var (_, token) = await SemearAsync(c, Local(c.Segunda, 10, 0));

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/publico/meus-agendamentos/{token}/cancelar");
        requisicao.Headers.Add("Prefer", "return=representation");
        var resposta = await c.Publico.SendAsync(requisicao);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Cancelado");
    }

    [Fact]
    public async Task Horario_liberado_pelo_cancelamento_aparece_para_outro_agendamento_remarcar()
    {
        var c = await ArranjarAsync();
        var (_, tokenA) = await SemearAsync(c, Local(c.Segunda, 10, 0));
        var (_, tokenB) = await SemearAsync(c, Local(c.Segunda, 14, 0));

        (await HorariosAsync(c, tokenB, c.Segunda)).Should().NotContain(Local(c.Segunda, 10, 0));
        (await c.Publico.PostAsync($"/publico/meus-agendamentos/{tokenA}/cancelar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await HorariosAsync(c, tokenB, c.Segunda)).Should().Contain(Local(c.Segunda, 10, 0));
    }

    // ---------------------------------------------------------------- apoio

    private sealed record Contexto(
        HttpClient Publico, Guid NegocioId, SemeadorDeAgenda.CenarioDeAgenda Cenario, DateOnly Segunda);

    private sealed record HorarioPublico(DateTimeOffset Inicio, Guid ProfissionalId);

    private async Task<Contexto> ArranjarAsync()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var negocio = await _fabrica.NoBancoAsync(db => db.Negocios.IgnoreQueryFilters().SingleAsync(n => n.Id == negocioId));
        return new Contexto(ClientePorSlug(negocio.Slug.Valor), negocioId, cenario, ProximaSegunda());
    }

    private async Task<(Guid Id, string Token)> SemearAsync(Contexto c, DateTimeOffset inicio, decimal[]? precos = null)
    {
        var id = await _fabrica.SemearAgendamentoAsync(c.NegocioId, c.Cenario.ProfissionalId, inicio, precos: precos, servicoId: c.Cenario.ServicoId);
        var token = _fabrica.Services.GetRequiredService<IServicoTokenPublico>().GerarTokenAgendamento(c.NegocioId, id);
        return (id, token);
    }

    private async Task<JsonElement> AcoesAsync(Contexto c, string token) =>
        (await c.Publico.GetFromJsonAsync<JsonElement>($"/publico/meus-agendamentos/{token}")).GetProperty("acoes");

    private async Task<List<DateTimeOffset>> HorariosAsync(Contexto c, string token, DateOnly dia) =>
        (await c.Publico.GetFromJsonAsync<JsonElement>($"/publico/meus-agendamentos/{token}/horarios-livres?data={dia:yyyy-MM-dd}"))
            .GetProperty("horarios").EnumerateArray().Select(h => h.GetDateTimeOffset()).ToList();

    private Task<StatusAgendamento> Status(Contexto c, Guid id) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().Where(a => a.Id == id).Select(a => a.Status).SingleAsync());

    private Task MudarStatusAsync(Guid id, string status) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var agendamento = await db.Agendamentos.IgnoreQueryFilters().Include(a => a.Servicos).SingleAsync(a => a.Id == id);
            switch (status)
            {
                case "Cancelado": agendamento.Cancelar(); break;
                case "Concluido": agendamento.MarcarConcluido(0m, DateTimeOffset.UtcNow); break;
                case "EmAtendimento": agendamento.IniciarAtendimento(); break;
                default: throw new ArgumentException(status);
            }

            return await db.SaveChangesAsync();
        });

    private async Task<(string Slug, Guid NegocioId)> CriarOutroNegocioAsync()
    {
        var slug = "outro-" + Guid.NewGuid().ToString("N")[..10];
        var negocio = Negocio.Criar(Slug.Criar(slug), "Outro Negócio", TipoNegocio.Barbearia);
        await _fabrica.NoBancoAsync(async db =>
        {
            db.Negocios.Add(negocio);
            return await db.SaveChangesAsync();
        });
        return (slug, negocio.Id);
    }

    private HttpClient ClientePorSlug(string slug) => _fabrica.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri($"http://{slug}.{DominioDeTeste.Valor}/"),
    });

    private static DateOnly ProximaSegunda()
    {
        var dia = SemeadorDeComissoes.Dia(3);
        while (dia.DayOfWeek != DayOfWeek.Monday)
            dia = dia.AddDays(1);
        return dia;
    }

    private static DateTimeOffset Local(DateOnly dia, int hora, int minuto) => SemeadorDeComissoes.Local(dia, hora, minuto);
}
