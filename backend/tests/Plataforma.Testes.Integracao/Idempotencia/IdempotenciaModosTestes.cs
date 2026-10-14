using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Idempotencia;

/// <summary>Chave opcional (padrão atual): sem o cabeçalho a operação segue como sempre foi. Mesmo contrato da base, em modo opcional.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class IdempotenciaOpcionalTestes(PostgresContainerFixture postgres) : IdempotenciaOperacoesBase(postgres, obrigatoria: false)
{
    [Theory, MemberData(nameof(Operacoes))]
    public async Task Sem_chave_preserva_o_comportamento_atual(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);

        var primeira = await Enviar(c, op, chave: null);
        var segunda = await Enviar(c, op, chave: null);

        primeira.StatusCode.Should().Be(op.Status);
        segunda.StatusCode.Should().Be(op.Status, "sem chave duas requisições são duas operações (como sempre foi)");
        (await op.Efeitos(this, c)).Should().Be(2);
        segunda.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        (await RegistrosAsync(c)).Should().Be(0, "sem chave a tabela não é tocada");
    }
}

/// <summary>
/// Fase 3A: <c>Idempotencia__Obrigatoria=true</c>. Toda a bateria da base (replay, 422, paralelismo, falha de rede, commit ambíguo,
/// isolamento por negócio e usuário) roda de novo com a chave obrigatória; aqui entra o que só existe nesse modo: recusa antes de
/// qualquer efeito, formato do erro, compatibilidade com o mecanismo desligado e o que fica fora do escopo.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class IdempotenciaObrigatoriaTestes(PostgresContainerFixture postgres) : IdempotenciaOperacoesBase(postgres, obrigatoria: true)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Sem_chave_e_recusado_com_400_estavel_antes_de_qualquer_efeito(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);
        var estoqueAntes = await EstoqueAsync(c);
        var emails = _fabrica.Services.GetRequiredService<EspiaEmail>();

        var primeira = await Enviar(c, op, chave: null);
        var segunda = await Enviar(c, op, chave: null);

        foreach (var resposta in new[] { primeira, segunda })
        {
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            resposta.Content.Headers.ContentType!.MediaType.Should().Contain("json");
            var problema = await resposta.Content.ReadFromJsonAsync<JsonElement>();
            problema.GetProperty("codigo").GetString().Should().Be("idempotency_key_obrigatoria");
            problema.GetProperty("status").GetInt32().Should().Be(400);
            var titulo = problema.GetProperty("title").GetString()!;
            titulo.Should().Contain("Idempotency-Key");
            titulo.Should().NotContainAny("Exception", "Npgsql", "SELECT", "StackTrace", "   at ");
            resposta.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        }

        (await op.Efeitos(this, c)).Should().Be(0, "recusado antes de qualquer efeito");
        (await EstoqueAsync(c)).Should().Be(estoqueAntes, "nenhum movimento de estoque");
        (await RegistrosAsync(c)).Should().Be(0, "nenhum registro parcial de idempotência");
        emails.Enviados.Should().BeEmpty("nenhuma notificação");
    }

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Chave_vazia_ou_so_com_espacos_e_recusada_e_nada_e_executado(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);

        foreach (var valor in new[] { "", "   " })
        {
            using var requisicao = Requisicao(c, op, chave: null, variante: 0, formatoAlternativo: false);
            requisicao.Headers.TryAddWithoutValidation("Idempotency-Key", valor);

            var resposta = await c.Admin.SendAsync(requisicao);

            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"valor '{valor}'");
            // O Kestrel entrega o cabeçalho vazio como ausente: a recusa é a de chave obrigatória (ou a de chave inválida, se um dia chegar).
            (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString()
                .Should().BeOneOf("idempotency_key_obrigatoria", "idempotency_key_invalida");
        }

        (await op.Efeitos(this, c)).Should().Be(0);
        (await RegistrosAsync(c)).Should().Be(0);
    }

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Nova_chave_com_payload_alterado_e_uma_nova_intencao_aceita(string nome)
    {
        var c = await ArranjarAsync();
        var op = Op(nome);
        if (op.SemCorpo)
            return;

        (await Enviar(c, op, NovaChave(), variante: 0)).StatusCode.Should().Be(op.Status);
        (await Enviar(c, op, NovaChave(), variante: 1)).StatusCode.Should().Be(op.Status);

        (await op.Efeitos(this, c)).Should().Be(2);
        (await RegistrosAsync(c)).Should().Be(2);
    }

    [Fact]
    public async Task Sem_login_continua_401_e_sem_permissao_continua_403_mesmo_sem_a_chave()
    {
        using var anonimo = _fabrica.CreateClient();
        var (semPermissao, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Profissional);
        var corpo = new LancarVenda([new ItemLancarVenda(Guid.NewGuid(), 1, 20m)], Guid.NewGuid(), null);

        (await anonimo.PostAsJsonAsync("/painel/vendas", corpo, Web)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await semPermissao.PostAsJsonAsync("/painel/vendas", corpo, Web)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoints_fora_do_escopo_continuam_funcionando_sem_a_chave()
    {
        var c = await ArranjarAsync();

        (await c.Admin.PostAsJsonAsync("/painel/clientes", new CriarCliente("Cliente Fora do Escopo", "+5571988887777"), Web))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await c.Admin.PostAsJsonAsync("/painel/agenda/clientes/rapido", new CadastroRapidoCliente("Cliente Rápido", "71988886666"), Web))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await c.Admin.PostAsJsonAsync($"/painel/estoque/produtos/{c.Produto}/ajustes", new RegistrarAjuste(50, "Contagem do mês"), Web))
            .IsSuccessStatusCode.Should().BeTrue();
        (await c.Admin.PostAsJsonAsync("/painel/agendamentos/forcados/previa",
            new ConsultaForcar(c.Agenda.ProfissionalId, [c.Agenda.ServicoId], Local(c, 19)), Web))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.Admin.GetAsync("/painel/agenda/clientes/busca?termo=fora")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.Admin.GetAsync("/painel/estoque/produtos")).StatusCode.Should().Be(HttpStatusCode.OK);
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RegistrosAsync(c)).Should().Be(0, "nada disso passa pelo mecanismo");
    }

    [Fact]
    public async Task Mecanismo_desligado_prevalece_sobre_a_obrigatoriedade_e_ignora_o_cabecalho()
    {
        await using var fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, new Dictionary<string, string?>
        {
            ["Idempotencia:Habilitada"] = "false",
            ["Idempotencia:Obrigatoria"] = "true",
        });
        using var aquecimento = fabrica.CreateClient();
        await fabrica.ResetarBancoAsync();
        var (admin, negocioId, _, _) = await fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var agenda = await fabrica.CriarCenarioPadraoAsync(negocioId);
        var corpo = new LancarVale(agenda.ProfissionalId, 30m);

        var semChave = await admin.PostAsJsonAsync("/painel/saldos/vales", corpo, Web);
        using var comChave = new HttpRequestMessage(HttpMethod.Post, "/painel/saldos/vales") { Content = JsonContent.Create(corpo, options: Web) };
        comChave.Headers.Add("Idempotency-Key", NovaChave());
        var comChaveResposta = await admin.SendAsync(comChave);

        semChave.StatusCode.Should().Be(HttpStatusCode.Created, "com o mecanismo desligado nada é exigido");
        comChaveResposta.StatusCode.Should().Be(HttpStatusCode.Created);
        comChaveResposta.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        (await fabrica.NoBancoAsync(db => db.RequisicoesIdempotentes.CountAsync(r => r.NegocioId == negocioId))).Should().Be(0);
    }

    [Theory, MemberData(nameof(Operacoes))]
    public async Task Cors_preflight_aceita_o_cabecalho_em_todos_os_endpoints_protegidos(string nome)
    {
        // O Program.cs lê Marca:Dominio cedo; a política de CORS só nasce com o domínio vindo do ambiente (restaurado logo depois).
        var anterior = Environment.GetEnvironmentVariable("Marca__Dominio");
        Environment.SetEnvironmentVariable("Marca__Dominio", DominioDeTeste.Valor);
        PlataformaWebApplicationFactory fabrica;
        HttpClient cliente;
        try
        {
            fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, new Dictionary<string, string?> { ["Idempotencia:Obrigatoria"] = "true" });
            cliente = fabrica.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable("Marca__Dominio", anterior);
        }

        await using var _ = fabrica;
        using var __ = cliente;
        var url = nome switch
        {
            "venda" => "/painel/vendas",
            "entrada" => $"/painel/estoque/produtos/{Guid.NewGuid()}/entradas",
            "consumo" => $"/painel/estoque/produtos/{Guid.NewGuid()}/consumos",
            "vale" => "/painel/saldos/vales",
            "forcado" => "/painel/agendamentos/forcados",
            "encaixe" => "/painel/encaixes",
            _ => $"/painel/fidelidade/clientes/{Guid.NewGuid()}/resgatar",
        };
        using var preflight = new HttpRequestMessage(HttpMethod.Options, url);
        preflight.Headers.Add("Origin", $"https://app.{DominioDeTeste.Valor}");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,idempotency-key");

        var resposta = await cliente.SendAsync(preflight);

        resposta.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);
        resposta.Headers.GetValues("Access-Control-Allow-Headers").SelectMany(v => v.Split(',')).Select(v => v.Trim().ToLowerInvariant())
            .Should().Contain("idempotency-key");
    }
}
