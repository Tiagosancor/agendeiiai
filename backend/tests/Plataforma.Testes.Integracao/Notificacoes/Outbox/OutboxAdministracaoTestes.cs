using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Administracao;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Notificacoes.Outbox;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Notificacoes.Outbox;

/// <summary>
/// B7.4-B: privacidade da outbox (só identificadores e códigos técnicos), contrato administrativo da plataforma e retenção de 30 dias.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class OutboxAdministracaoTestes(PostgresContainerFixture postgres) : OutboxTestesBase(postgres)
{
    private readonly CapturaDeLogs _logs = new();
    private readonly RegistroDeContextos _contextos = new();

    protected override void ConfigurarServicos(IServiceCollection servicos) => servicos.AdicionarEspioes(_contextos, _logs);

    private static readonly string[] DadosPessoais =
    [
        "Fulano Sigiloso", "fulano.sigiloso@exemplo.com", "71999990000", "Anotação íntima do cliente", "Dra. Prof Sigilosa",
        "prof.sigilosa@exemplo.com", "71988887777", "Corte Premium Sigiloso", "12345678909",
    ];

    // ------------------------------------------------------------------ 43–44 privacidade

    [Fact]
    public async Task Payload_da_outbox_nunca_guarda_dado_pessoal_nem_texto_de_mensagem()
    {
        var c = await ArranjarAsync();
        await SqlAsync($"UPDATE clientes SET nome = 'Fulano Sigiloso', email = 'fulano.sigiloso@exemplo.com', telefone = '+5571999990000' WHERE id = {c.ClienteId}");
        await SqlAsync($"UPDATE profissionais SET nome = 'Dra. Prof Sigilosa', email = 'prof.sigilosa@exemplo.com', telefone = '71988887777' WHERE id = {c.ProfissionalId}");
        await SqlAsync($"UPDATE servicos SET nome = 'Corte Premium Sigiloso' WHERE id = {c.ServicoId}");

        var id = (await ComServicoAsync(c.NegocioId, s => s.CriarAsync(new Plataforma.Aplicacao.Agendamentos.CriarAgendamento(
            c.ProfissionalId, c.ClienteId, [c.ServicoId], Hora(c, 10), "Anotação íntima do cliente")))).AgendamentoId!.Value;
        await ComServicoAsync(c.NegocioId, s => s.MoverAsync(id, Hora(c, 11)));
        await ComServicoAsync(c.NegocioId, s => s.CancelarAvisandoClienteAsync(id));

        var linhas = await LinhasDaTabelaAsync();
        linhas.Should().NotBeEmpty();
        foreach (var linha in linhas)
            foreach (var dado in DadosPessoais)
                linha.Should().NotContainEquivalentOf(dado, "a outbox guarda só identificadores e metadados técnicos");

        // E depois do despacho (que monta o conteúdo e grava o id do provedor): continua sem nada pessoal.
        await DespacharAsync();
        foreach (var linha in await LinhasDaTabelaAsync())
            foreach (var dado in DadosPessoais)
                linha.Should().NotContainEquivalentOf(dado);

        // Só as colunas esperadas existem: nenhuma de nome, e-mail, telefone, texto ou observação.
        var colunas = await Fabrica.NoBancoAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'notificacoes_pendentes'").ToListAsync());
        colunas.Should().NotContain(coluna => coluna.Contains("nome") || coluna.Contains("email") || coluna.Contains("telefone")
            || coluna.Contains("texto") || coluna.Contains("mensagem") && coluna != "id_mensagem_provedor" || coluna.Contains("observ") || coluna.Contains("cpf"));
    }

    [Fact]
    public async Task Erro_persistido_e_log_de_falha_nao_trazem_dado_pessoal()
    {
        var c = await ArranjarAsync();
        await SqlAsync($"UPDATE clientes SET email = 'fulano.sigiloso@exemplo.com' WHERE id = {c.ClienteId}");
        Email.Falhar = true;
        Email.StatusDaFalha = HttpStatusCode.BadRequest; // definitiva, com a mensagem da exceção carregando o e-mail do cliente
        WhatsApp.Resultado = () => throw new InvalidOperationException($"explodiu com {Pii}");
        var id = (await ComServicoAsync(c.NegocioId, s => s.CriarReservaPublicaAsync(new Plataforma.Aplicacao.Agendamentos.CriarReservaPublica(c.ProfissionalId, [c.ServicoId], Hora(c, 10))))).AgendamentoId!.Value;
        await ComServicoAsync(c.NegocioId, s => s.ConfirmarReservaPublicaAsync(new Plataforma.Aplicacao.Agendamentos.ConfirmarReservaPublica(
            id, "Fulano Sigiloso", "+5571999990000", "fulano.sigiloso@exemplo.com", null, null, "127.0.0.1")));

        await DespacharAsync();

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Should().OnlyContain(i => i.Status == StatusNotificacao.Falhou || i.Status == StatusNotificacao.Desconhecida);
        itens.Select(i => i.UltimoErroCodigo).Should().OnlyContain(codigo => codigo == CodigosOutbox.RecusadaProvedor || codigo == CodigosOutbox.TimeoutAmbiguo);

        var texto = string.Join('\n', _logs.Linhas) + string.Join('\n', await LinhasDaTabelaAsync());
        texto.Should().NotContain("Sigiloso").And.NotContain("sigiloso").And.NotContain("71999990000");
        _logs.Linhas.Should().Contain(l => l.Contains("falhou definitivamente") && l.Contains(id.ToString()) == false && l.Contains(c.NegocioId.ToString()));
    }

    // ------------------------------------------------------------------ 45–47 contrato administrativo

    [Fact]
    public async Task Contrato_administrativo_exige_o_dono_da_plataforma()
    {
        var c = await ArranjarAsync();
        using var anonimo = Fabrica.CreateClient();

        foreach (var rota in new[] { "/plataforma/notificacoes/resumo", "/plataforma/notificacoes/outbox" })
        {
            (await anonimo.GetAsync(rota)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // O token do painel (um negócio qualquer, mesmo Administrador) não serve: esquema e audiência próprios da plataforma.
            (await c.Admin.GetAsync(rota)).StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        }

        using var dono = await LogarComoDonoAsync();
        (await dono.GetAsync("/plataforma/notificacoes/resumo")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await dono.GetAsync("/plataforma/notificacoes/outbox")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resumo_mostra_contagens_idade_da_pendencia_mais_antiga_e_as_falhas_recentes_sem_dado_pessoal()
    {
        var c = await ArranjarAsync();
        var agora = DateTimeOffset.UtcNow;
        await SemearItemAsync(c.NegocioId, StatusNotificacao.Pendente, atualizadoEm: agora, criadoEm: agora.AddMinutes(-30));
        await SemearItemAsync(c.NegocioId, StatusNotificacao.Pendente, atualizadoEm: agora, criadoEm: agora.AddMinutes(-5));
        await SemearItemAsync(c.NegocioId, StatusNotificacao.Enviando, atualizadoEm: agora);
        await SemearItemAsync(c.NegocioId, StatusNotificacao.Falhou, atualizadoEm: agora.AddMinutes(-2), erro: CodigosOutbox.RecusadaProvedor);
        await SemearItemAsync(c.NegocioId, StatusNotificacao.Desconhecida, atualizadoEm: agora.AddMinutes(-1), erro: CodigosOutbox.TimeoutAmbiguo);
        await SemearItemAsync(c.NegocioId, StatusNotificacao.Enviada, atualizadoEm: agora);

        using var dono = await LogarComoDonoAsync();
        var texto = await dono.GetStringAsync("/plataforma/notificacoes/resumo");
        var resumo = JsonDocument.Parse(texto).RootElement;

        resumo.GetProperty("pendentes").GetInt32().Should().Be(2);
        resumo.GetProperty("enviando").GetInt32().Should().Be(1);
        resumo.GetProperty("falhas").GetInt32().Should().Be(1);
        resumo.GetProperty("desconhecidas").GetInt32().Should().Be(1);
        resumo.GetProperty("idadeDaPendenciaMaisAntigaSegundos").GetDouble().Should().BeInRange(29 * 60, 32 * 60);
        var recentes = resumo.GetProperty("falhasRecentes").EnumerateArray().ToList();
        recentes.Should().HaveCount(2);
        recentes[0].GetProperty("status").GetString().Should().Be("Desconhecida", "da mais nova para a mais antiga");

        foreach (var dado in DadosPessoais)
            texto.Should().NotContainEquivalentOf(dado);
        recentes[0].EnumerateObject().Select(p => p.Name).Should().NotContain(["nome", "email", "telefone", "texto", "mensagem", "observacoes"]);
    }

    [Fact]
    public async Task Lista_pagina_e_filtra_por_status_tipo_canal_negocio_e_periodo()
    {
        var a = await ArranjarAsync();
        var b = await ArranjarAsync();
        var agora = DateTimeOffset.UtcNow;
        for (var i = 0; i < 12; i++)
            await SemearItemAsync(a.NegocioId, StatusNotificacao.Falhou, atualizadoEm: agora.AddMinutes(-i), erro: CodigosOutbox.RecusadaProvedor);
        for (var i = 0; i < 3; i++)
            await SemearItemAsync(b.NegocioId, StatusNotificacao.Enviada, atualizadoEm: agora.AddDays(-2 - i), canal: CanalNotificacao.WhatsApp);
        await SemearItemAsync(b.NegocioId, StatusNotificacao.Falhou, atualizadoEm: agora.AddDays(-10), tipo: TipoNotificacao.LembreteRetorno, canal: CanalNotificacao.WhatsApp);

        using var dono = await LogarComoDonoAsync();

        var pagina1 = await dono.GetFromJsonAsync<JsonElement>("/plataforma/notificacoes/outbox?status=Falhou&tamanho=5&pagina=1");
        var pagina3 = await dono.GetFromJsonAsync<JsonElement>("/plataforma/notificacoes/outbox?status=Falhou&tamanho=5&pagina=3");
        pagina1.GetProperty("total").GetInt32().Should().Be(13);
        pagina1.GetProperty("itens").GetArrayLength().Should().Be(5);
        pagina3.GetProperty("itens").GetArrayLength().Should().Be(3);
        pagina1.GetProperty("itens")[0].GetProperty("tipo").GetString().Should().NotBeNullOrEmpty();

        (await dono.GetFromJsonAsync<JsonElement>($"/plataforma/notificacoes/outbox?negocioId={b.NegocioId}")).GetProperty("total").GetInt32().Should().Be(4);
        (await dono.GetFromJsonAsync<JsonElement>("/plataforma/notificacoes/outbox?canal=WhatsApp")).GetProperty("total").GetInt32().Should().Be(4);
        (await dono.GetFromJsonAsync<JsonElement>("/plataforma/notificacoes/outbox?tipo=LembreteRetorno")).GetProperty("total").GetInt32().Should().Be(1);

        var de = Uri.EscapeDataString(agora.AddDays(-5).ToString("O"));
        var ate = Uri.EscapeDataString(agora.AddDays(-1).ToString("O"));
        (await dono.GetFromJsonAsync<JsonElement>($"/plataforma/notificacoes/outbox?de={de}&ate={ate}")).GetProperty("total").GetInt32().Should().Be(3);

        // Tamanho acima do teto é limitado, página inválida vira a primeira.
        var limitada = await dono.GetFromJsonAsync<JsonElement>("/plataforma/notificacoes/outbox?tamanho=100000&pagina=-4");
        limitada.GetProperty("tamanho").GetInt32().Should().Be(200);
        limitada.GetProperty("pagina").GetInt32().Should().Be(1);
    }

    // ------------------------------------------------------------------ 48–49 retenção

    [Fact]
    public async Task Limpeza_apaga_os_terminados_apos_30_dias_e_nunca_os_ativos()
    {
        var c = await ArranjarAsync();
        var agora = DateTimeOffset.UtcNow;
        var velhos = new[] { StatusNotificacao.Enviada, StatusNotificacao.Cancelada, StatusNotificacao.Desconhecida, StatusNotificacao.Falhou };
        foreach (var status in velhos)
        {
            await SemearItemAsync(c.NegocioId, status, atualizadoEm: agora.AddDays(-31));
            await SemearItemAsync(c.NegocioId, status, atualizadoEm: agora.AddDays(-29));
        }

        // Ativos, com qualquer idade: pendente e em processamento (mesmo com lease vencido) nunca são apagados.
        var pendente = await SemearItemAsync(c.NegocioId, StatusNotificacao.Pendente, atualizadoEm: agora.AddDays(-100), criadoEm: agora.AddDays(-100));
        var enviando = await SemearItemAsync(c.NegocioId, StatusNotificacao.Enviando, atualizadoEm: agora.AddDays(-100), criadoEm: agora.AddDays(-100));

        int removidos;
        using (var escopo = Fabrica.Services.CreateScope())
            removidos = await escopo.ServiceProvider.GetRequiredService<JobLimparNotificacoes>().ExecutarAsync();

        removidos.Should().Be(4);
        var restantes = await ItensAsync();
        restantes.Should().HaveCount(6);
        restantes.Should().Contain(i => i.Id == pendente).And.Contain(i => i.Id == enviando);
        restantes.Where(i => i.Status is StatusNotificacao.Enviada or StatusNotificacao.Cancelada or StatusNotificacao.Desconhecida or StatusNotificacao.Falhou)
            .Should().OnlyContain(i => i.AtualizadoEm > agora.AddDays(-30), "só os de menos de 30 dias ficaram");
    }

    [Fact]
    public async Task Limpeza_apaga_todos_os_terminados_vencidos_de_uma_vez()
    {
        var c = await ArranjarAsync();
        var agora = DateTimeOffset.UtcNow;
        for (var i = 0; i < 25; i++)
            await SemearItemAsync(c.NegocioId, StatusNotificacao.Enviada, atualizadoEm: agora.AddDays(-45));

        using (var escopo = Fabrica.Services.CreateScope())
            (await escopo.ServiceProvider.GetRequiredService<JobLimparNotificacoes>().ExecutarAsync()).Should().Be(25);

        (await ItensAsync()).Should().BeEmpty();
    }

    // ------------------------------------------------------------------ apoio

    private async Task<HttpClient> LogarComoDonoAsync()
    {
        using (var escopo = Fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<IAdministracaoPlataforma>()
                .CriarOuRedefinirAdministradorAsync("dono-outbox@plataforma.dev", "Dono", "SenhaForte123");

        using var anonimo = Fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/plataforma/auth/login", new { email = "dono-outbox@plataforma.dev", senha = "SenhaForte123" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        var dono = Fabrica.CreateClient();
        dono.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return dono;
    }

    private Task<List<string>> LinhasDaTabelaAsync() =>
        Fabrica.NoBancoAsync(db => db.Database
            .SqlQueryRaw<string>("SELECT row_to_json(n)::text AS \"Value\" FROM notificacoes_pendentes n").ToListAsync());

    /// <summary>Insere um item direto no estado pedido (respeitando o check do lease), com as datas controladas.</summary>
    private async Task<Guid> SemearItemAsync(
        Guid negocioId, StatusNotificacao status, DateTimeOffset atualizadoEm, DateTimeOffset? criadoEm = null, string? erro = null,
        TipoNotificacao tipo = TipoNotificacao.Confirmacao, CanalNotificacao canal = CanalNotificacao.Email)
    {
        var id = Guid.NewGuid();
        var atualizado = atualizadoEm.ToUniversalTime();
        var criado = (criadoEm ?? atualizadoEm).ToUniversalTime();
        var expira = DateTimeOffset.UtcNow.AddDays(1).ToUniversalTime();
        var enviando = status == StatusNotificacao.Enviando;
        DateTimeOffset? lease = enviando ? atualizado.AddMinutes(2) : null;
        Guid? token = enviando ? Guid.NewGuid() : null;
        var chave = $"teste:{id}";
        var tipoTexto = tipo.ToString();
        var canalTexto = canal.ToString();
        var statusTexto = status.ToString();

        await SqlAsync($@"INSERT INTO notificacoes_pendentes
                (id, negocio_id, tipo, canal, chave_deduplicacao, status, tentativas, proxima_tentativa_em, expira_em, lease_ate, reivindicacao_id,
                 ultimo_erro_codigo, agendamento_id, criado_em, atualizado_em)
            VALUES ({id}, {negocioId}, {tipoTexto}, {canalTexto}, {chave}, {statusTexto}, 1, {atualizado}, {expira}, {lease}::timestamptz, {token}::uuid,
                 {erro}, {Guid.NewGuid()}, {criado}, {atualizado})");
        return id;
    }
}
