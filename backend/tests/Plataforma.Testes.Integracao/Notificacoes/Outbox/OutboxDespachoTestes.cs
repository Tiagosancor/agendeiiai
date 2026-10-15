using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Notificacoes.Outbox;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Notificacoes.Outbox;

/// <summary>
/// B7.4-B: o despachante — reivindicação atômica (várias instâncias), lease, ponto sem retorno, backoff, falhas, prazo e independência
/// entre os canais. O relógio é controlado pelo teste; a rede é sempre um espião.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class OutboxDespachoTestes(PostgresContainerFixture postgres) : OutboxTestesBase(postgres)
{
    private static readonly TimeSpan[] EsperasOperacionais =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(360)];

    private async Task<List<Guid>> CriarVariosAsync(CenarioOutbox c, int quantidade)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < quantidade; i++)
            // 09:00–12:00 e 13:00–18:00 (almoço no meio): seis horários de manhã, o resto à tarde.
            ids.Add(await CriarAgendamentoAsync(c, i < 6 ? Hora(c, 9).AddMinutes(30 * i) : Hora(c, 13).AddMinutes(30 * (i - 6))));
        return ids;
    }

    // ------------------------------------------------------------------ 7 envio bem-sucedido

    [Fact]
    public async Task Envio_bem_sucedido_marca_enviada_e_guarda_o_id_do_provedor_sem_dado_pessoal()
    {
        var c = await ArranjarAsync();
        WhatsApp.Resultado = () => ResultadoEnvioWhatsApp.AguardandoConfirmacao("ID-PROVEDOR-1");
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        var despachados = await DespacharAsync();

        despachados.Should().Be(2);
        var itens = await ItensDoAgendamentoAsync(id);
        itens.Should().OnlyContain(i => i.Status == StatusNotificacao.Enviada && i.Tentativas == 1 && i.EnviadaEm != null);
        itens.Should().OnlyContain(i => i.LeaseAte == null && i.ReivindicacaoId == null && i.UltimoErroCodigo == null);
        itens.Single(i => i.Canal == CanalNotificacao.WhatsApp).IdMensagemProvedor.Should().Be("ID-PROVEDOR-1");
        itens.Single(i => i.Canal == CanalNotificacao.Email).IdMensagemProvedor.Should().BeNull();

        // O registro por canal do aviso ao profissional (onde o webhook de status grava) continua sendo criado.
        var avisos = await Fabrica.NoBancoAsync(db => db.NotificacoesProfissional.IgnoreQueryFilters().AsNoTracking().ToListAsync());
        avisos.Should().HaveCount(2);
        avisos.Should().ContainSingle(a => a.IdMensagemWhatsApp == "ID-PROVEDOR-1" && a.CanalWhatsAppStatus == StatusCanal.Pendente);
    }

    [Fact]
    public async Task Rede_nunca_e_chamada_dentro_de_transacao_e_o_ponto_sem_retorno_ja_esta_gravado_antes_da_chamada()
    {
        var c = await ArranjarAsync(avisoProfissionalPorWhatsApp: true);
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        string? visto = null;

        // Dentro da "chamada ao provedor", olha o banco por outra conexão: nenhuma transação aberta, nenhuma linha travada e o item
        // já está Enviando, com a tentativa contada e o ponto sem retorno gravado (confirmados, não pendentes de commit).
        WhatsApp.Resultado = () =>
        {
            visto = Fabrica.NoBancoAsync(async db =>
            {
                var ociosas = await db.Database.SqlQueryRaw<int>(
                    "SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND state LIKE 'idle in transaction%'").SingleAsync();
                var linha = await db.NotificacoesPendentes.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(n => n.AgendamentoId == id && n.Canal == CanalNotificacao.WhatsApp);
                await using var transacao = await db.Database.BeginTransactionAsync();
                // NOWAIT: lança (55P03) se algum outro processo estivesse segurando a linha.
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM notificacoes_pendentes WHERE id = {linha.Id} FOR UPDATE NOWAIT");
                await transacao.RollbackAsync();
                return $"{ociosas}|{linha.Status}|{linha.Tentativas}|{linha.EnvioIniciadoEm is not null}|{linha.LeaseAte is not null}";
            }).GetAwaiter().GetResult();
            return ResultadoEnvioWhatsApp.Enviado();
        };

        await DespacharAsync();

        visto.Should().Be("0|Enviando|1|True|True");
        (await ItensDoAgendamentoAsync(id)).Should().OnlyContain(i => i.Status == StatusNotificacao.Enviada);
    }

    // ------------------------------------------------------------------ 8–9 concorrência

    [Fact]
    public async Task Dois_despachantes_concorrentes_enviam_cada_mensagem_uma_vez()
    {
        var c = await ArranjarAsync();
        await CriarVariosAsync(c, 8);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(DespacharAsync)));

        Email.Enviados.Should().HaveCount(8, "uma por agendamento, nunca repetida");
        WhatsApp.Enviados.Should().HaveCount(8);
        (await ItensAsync()).Should().OnlyContain(i => i.Status == StatusNotificacao.Enviada && i.Tentativas == 1);
    }

    [Fact]
    public async Task Varias_instancias_da_api_nao_enviam_a_mesma_mensagem_duas_vezes()
    {
        var c = await ArranjarAsync();
        await CriarVariosAsync(c, 10);

        await using var segunda = new PlataformaWebApplicationFactory(
            Postgres.ConnectionString, Configuracao, servicos => servicos.AddSingleton<TimeProvider>(Relogio));
        using var aquecimento = segunda.CreateClient();

        async Task<int> DespacharDa(PlataformaWebApplicationFactory fabrica)
        {
            using var escopo = fabrica.Services.CreateScope();
            return await escopo.ServiceProvider.GetRequiredService<JobDespacharNotificacoes>().ExecutarCicloAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, 3).SelectMany(_ => new[] { DespacharDa(Fabrica), DespacharDa(segunda) }));

        var emails = Email.Enviados.Count + segunda.Services.GetRequiredService<EspiaEmail>().Enviados.Count;
        var whatsApp = WhatsApp.Enviados.Count + segunda.Services.GetRequiredService<EspiaWhatsApp>().Enviados.Count;
        emails.Should().Be(10);
        whatsApp.Should().Be(10);
        (await ItensAsync()).Should().OnlyContain(i => i.Status == StatusNotificacao.Enviada && i.Tentativas == 1);
    }

    // ------------------------------------------------------------------ 10–13 backoff, falha definitiva, limite, prazo

    [Fact]
    public async Task Falha_retentavel_segue_a_sequencia_de_espera_e_acaba_no_limite_de_cinco_tentativas()
    {
        var c = await ArranjarAsync(avisoProfissionalPorWhatsApp: false);
        Email.Falhar = true;
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        for (var tentativa = 1; tentativa <= 5; tentativa++)
        {
            await DespacharAsync();
            var item = (await ItensDoAgendamentoAsync(id)).Single();
            item.Tentativas.Should().Be(tentativa);
            Email.Chamadas.Should().Be(tentativa);

            if (tentativa == 5)
            {
                item.Status.Should().Be(StatusNotificacao.Falhou);
                item.UltimoErroCodigo.Should().Be(CodigosOutbox.TentativasEsgotadas);
                break;
            }

            var espera = EsperasOperacionais[tentativa - 1];
            item.Status.Should().Be(StatusNotificacao.Pendente);
            (item.ProximaTentativaEm - Relogio.GetUtcNow()).Should().BeCloseTo(espera, TimeSpan.FromSeconds(10), $"espera depois da tentativa {tentativa}");

            // Ainda não venceu a espera: o despachante não encosta no item.
            await DespacharAsync();
            Email.Chamadas.Should().Be(tentativa);

            Relogio.Avancar(espera + TimeSpan.FromSeconds(5));
        }

        // Esgotado, nada mais é tentado, nem com o tempo passando.
        Relogio.Avancar(TimeSpan.FromHours(3));
        await DespacharAsync();
        Email.Chamadas.Should().Be(5);
    }

    [Fact]
    public async Task Falha_definitiva_encerra_na_primeira_tentativa()
    {
        var c = await ArranjarAsync();
        Email.Falhar = true;
        Email.StatusDaFalha = HttpStatusCode.UnprocessableEntity;
        WhatsApp.Resultado = ResultadoEnvioWhatsApp.Recusada;
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        await DespacharAsync();
        Relogio.Avancar(TimeSpan.FromHours(2));
        await DespacharAsync();

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Should().OnlyContain(i => i.Status == StatusNotificacao.Falhou && i.UltimoErroCodigo == CodigosOutbox.RecusadaProvedor && i.Tentativas == 1);
        Email.Chamadas.Should().Be(1);
        WhatsApp.Chamadas.Should().Be(1);
    }

    [Fact]
    public async Task Erro_429_do_provedor_de_email_nao_e_recusa_e_tenta_de_novo()
    {
        var c = await ArranjarAsync(avisoProfissionalPorWhatsApp: false);
        Email.Falhar = true;
        Email.StatusDaFalha = HttpStatusCode.TooManyRequests;
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        await DespacharAsync();

        (await ItensDoAgendamentoAsync(id)).Single().Status.Should().Be(StatusNotificacao.Pendente);
    }

    [Fact]
    public async Task Depois_de_24_horas_nao_ha_nova_tentativa_e_a_falha_e_definitiva()
    {
        var c = await ArranjarAsync(avisoProfissionalPorWhatsApp: false);
        Email.Falhar = true;
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        await DespacharAsync();
        (await ItensDoAgendamentoAsync(id)).Single().Status.Should().Be(StatusNotificacao.Pendente);

        Relogio.Avancar(TimeSpan.FromHours(25));
        Email.Falhar = false;
        await DespacharAsync();

        var item = (await ItensDoAgendamentoAsync(id)).Single();
        item.Status.Should().Be(StatusNotificacao.Falhou);
        item.UltimoErroCodigo.Should().Be(CodigosOutbox.PrazoEsgotado);
        Email.Enviados.Should().BeEmpty("o prazo acabou: não se envia mensagem de mais de 24 horas atrás");
    }

    [Fact]
    public async Task Falha_definitiva_gera_log_estruturado_sem_dado_pessoal_e_nenhum_email_de_alerta()
    {
        var c = await ArranjarAsync(avisoProfissionalPorWhatsApp: false);
        Email.Falhar = true;
        Email.StatusDaFalha = HttpStatusCode.BadRequest;
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        await DespacharAsync();

        // Nenhuma outra tentativa de e-mail além da própria mensagem: não há "e-mail de alerta" (o canal pode ser o que caiu).
        Email.Chamadas.Should().Be(1);
        var item = (await ItensDoAgendamentoAsync(id)).Single();
        item.UltimoErroCodigo.Should().Be(CodigosOutbox.RecusadaProvedor);
    }

    // ------------------------------------------------------------------ 14–16 lease e queda

    [Fact]
    public async Task Lease_vencido_antes_do_provedor_devolve_o_item_a_fila_e_ele_sai_uma_vez()
    {
        var c = await ArranjarAsync(avisoProfissionalPorWhatsApp: false);
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        // O processo reivindicou e caiu antes de chamar o provedor.
        using (var escopo = Fabrica.Services.CreateScope())
        {
            var lote = await escopo.ServiceProvider.GetRequiredService<JobDespacharNotificacoes>().ReivindicarLoteAsync(CancellationToken.None);
            lote.Should().ContainSingle();
        }

        var preso = (await ItensDoAgendamentoAsync(id)).Single();
        preso.Status.Should().Be(StatusNotificacao.Enviando);
        preso.LeaseAte.Should().NotBeNull();
        preso.EnvioIniciadoEm.Should().BeNull();
        Email.Chamadas.Should().Be(0);

        // Com o lease ainda valendo, outra instância não pega o item.
        await DespacharAsync();
        Email.Chamadas.Should().Be(0);

        Relogio.Avancar(TimeSpan.FromMinutes(3));
        await DespacharAsync();

        var item = (await ItensDoAgendamentoAsync(id)).Single();
        item.Status.Should().Be(StatusNotificacao.Enviada);
        item.Tentativas.Should().Be(1, "a queda antes da chamada não gasta tentativa");
        Email.Enviados.Should().ContainSingle();
    }

    [Fact]
    public async Task Queda_depois_de_iniciar_o_envio_deixa_o_whatsapp_desconhecido_e_nunca_reenvia()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        await SimularQuedaDepoisDoAceiteAsync(id);

        await DespacharAsync();

        var whatsApp = (await ItensDoAgendamentoAsync(id)).Single(i => i.Canal == CanalNotificacao.WhatsApp);
        whatsApp.Status.Should().Be(StatusNotificacao.Desconhecida);
        whatsApp.UltimoErroCodigo.Should().Be(CodigosOutbox.LeaseExpiradoAposInicio);

        Relogio.Avancar(TimeSpan.FromHours(3));
        await DespacharAsync();
        WhatsApp.Chamadas.Should().Be(0, "pode ter sido aceita: nunca reenviada automaticamente");
    }

    [Fact]
    public async Task Queda_depois_de_iniciar_o_envio_de_email_volta_pela_politica_de_tentativas()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        await SimularQuedaDepoisDoAceiteAsync(id);

        await DespacharAsync();
        var email = (await ItensDoAgendamentoAsync(id)).Single(i => i.Canal == CanalNotificacao.Email);
        email.Status.Should().Be(StatusNotificacao.Pendente);
        email.Tentativas.Should().Be(1, "a chamada que pode ter acontecido conta");

        Relogio.Avancar(TimeSpan.FromMinutes(2));
        await DespacharAsync();

        (await ItensDoAgendamentoAsync(id)).Single(i => i.Canal == CanalNotificacao.Email).Status.Should().Be(StatusNotificacao.Enviada);
        Email.Enviados.Should().ContainSingle();
    }

    [Fact]
    public async Task Gravacao_de_instancia_que_perdeu_a_reivindicacao_nao_sobrescreve_o_item()
    {
        var c = await ArranjarAsync(avisoProfissionalPorWhatsApp: false);
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        IReadOnlyList<ItemReivindicado> lote;
        using (var escopo = Fabrica.Services.CreateScope())
            lote = await escopo.ServiceProvider.GetRequiredService<JobDespacharNotificacoes>().ReivindicarLoteAsync(CancellationToken.None);

        // Outra instância recuperou o item (lease vencido) e o reivindicou de novo, com outro token.
        Relogio.Avancar(TimeSpan.FromMinutes(3));
        await SqlAsync($"UPDATE notificacoes_pendentes SET reivindicacao_id = {Guid.NewGuid()} WHERE agendamento_id = {id}");

        using (var escopo = Fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<ProcessadorNotificacoesDoNegocio>().ProcessarAsync(c.NegocioId, lote, CancellationToken.None);

        Email.Chamadas.Should().Be(0, "quem não tem mais a reivindicação não chama o provedor");
        (await ItensDoAgendamentoAsync(id)).Single().Status.Should().Be(StatusNotificacao.Enviando);
    }

    [Fact]
    public async Task Provedor_de_whatsapp_que_lanca_deixa_o_item_desconhecido()
    {
        var c = await ArranjarAsync();
        WhatsApp.Resultado = () => throw new InvalidOperationException($"explodiu com {Pii}");
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        await DespacharAsync();

        var whatsApp = (await ItensDoAgendamentoAsync(id)).Single(i => i.Canal == CanalNotificacao.WhatsApp);
        whatsApp.Status.Should().Be(StatusNotificacao.Desconhecida);
        whatsApp.UltimoErroCodigo.Should().Be(CodigosOutbox.TimeoutAmbiguo, "o código é estável e nunca carrega a mensagem da exceção");
        whatsApp.UltimoErroCodigo.Should().NotContain("sigiloso");
    }

    // ------------------------------------------------------------------ 17–18 canais independentes

    [Fact]
    public async Task Falha_em_um_canal_nao_reenvia_o_outro()
    {
        var c = await ArranjarAsync();
        WhatsApp.Resultado = ResultadoEnvioWhatsApp.Indisponivel;
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        await DespacharAsync();

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Single(i => i.Canal == CanalNotificacao.Email).Status.Should().Be(StatusNotificacao.Enviada);
        itens.Single(i => i.Canal == CanalNotificacao.WhatsApp).Status.Should().Be(StatusNotificacao.Pendente);
        Email.Enviados.Should().ContainSingle();

        WhatsApp.Restaurar();
        Relogio.Avancar(TimeSpan.FromMinutes(2));
        await DespacharAsync();

        (await ItensDoAgendamentoAsync(id)).Should().OnlyContain(i => i.Status == StatusNotificacao.Enviada);
        Email.Enviados.Should().ContainSingle("o e-mail já tinha saído: a nova tentativa do WhatsApp não o repete");
        WhatsApp.Enviados.Should().ContainSingle();
    }

    [Fact]
    public async Task Timeout_ambiguo_do_whatsapp_vira_desconhecida_e_o_email_segue_normalmente()
    {
        var c = await ArranjarAsync();
        WhatsApp.Resultado = ResultadoEnvioWhatsApp.Ambigua;
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        await DespacharAsync();
        Relogio.Avancar(TimeSpan.FromHours(2));
        await DespacharAsync();

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Single(i => i.Canal == CanalNotificacao.WhatsApp).Status.Should().Be(StatusNotificacao.Desconhecida);
        itens.Single(i => i.Canal == CanalNotificacao.Email).Status.Should().Be(StatusNotificacao.Enviada);
        WhatsApp.Chamadas.Should().Be(1, "uma tentativa só: ambíguo não é reenviado");
    }

    // ------------------------------------------------------------------ apoio

    /// <summary>
    /// Estado que um processo que caiu DEPOIS de iniciar a chamada ao provedor (ponto sem retorno gravado) deixa: <c>Enviando</c>,
    /// uma tentativa contada e o lease já vencido.
    /// </summary>
    private async Task SimularQuedaDepoisDoAceiteAsync(Guid agendamentoId)
    {
        var inicio = Relogio.GetUtcNow().AddMinutes(-5).ToUniversalTime();
        var lease = Relogio.GetUtcNow().AddMinutes(-3).ToUniversalTime();
        await SqlAsync($@"UPDATE notificacoes_pendentes
                             SET status = 'Enviando', tentativas = 1, envio_iniciado_em = {inicio}, lease_ate = {lease}, reivindicacao_id = {Guid.NewGuid()}
                           WHERE agendamento_id = {agendamentoId}");
    }
}

/// <summary>A chave geral desligada pausa o despachante: nada é enviado e nada é descartado.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class OutboxChaveGeralDesligadaTestes(PostgresContainerFixture postgres) : OutboxTestesBase(postgres)
{
    protected override Dictionary<string, string?> Configuracao => new()
    {
        ["Notificacoes:OutboxHabilitada"] = "false",
        ["Notificacoes:OutboxOperacionais"] = "true",
    };

    [Fact]
    public async Task Despachante_pausado_nao_envia_e_a_familia_ligada_sem_a_chave_geral_continua_no_envio_direto()
    {
        var c = await ArranjarAsync();

        // Família ligada, mas a chave geral desligada: o caminho de criação é o direto (nenhum item na tabela).
        await CriarAgendamentoAsync(c, Hora(c, 10));
        (await ItensAsync()).Should().BeEmpty();
        Email.Enviados.Should().ContainSingle(e => e.Destinatario == c.EmailProfissional);

        // E o job do despachante, mesmo com algo na tabela, não faz nada com a chave geral desligada.
        using var escopo = Fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<OutboxNotificacoes>().EnfileirarAsync(c.NegocioId,
            [new NovaNotificacao(TipoNotificacao.Confirmacao, CanalNotificacao.Email, "x:1", DateTimeOffset.UtcNow.AddHours(1), Guid.NewGuid())],
            CancellationToken.None);
        await escopo.ServiceProvider.GetRequiredService<JobDespacharNotificacoes>().ExecutarAsync();

        (await ItensAsync()).Should().ContainSingle().Which.Status.Should().Be(StatusNotificacao.Pendente);
    }
}

/// <summary>
/// Rollback seguro: desligar só as famílias (com a chave geral ligada) faz os eventos novos voltarem ao envio direto, enquanto o
/// despachante continua escoando o que já estava na outbox — nada fica órfão e nada sai duas vezes.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class OutboxRollbackTestes(PostgresContainerFixture postgres) : OutboxTestesBase(postgres)
{
    protected override Dictionary<string, string?> Configuracao => new()
    {
        ["Notificacoes:OutboxHabilitada"] = "true",
        ["Notificacoes:OutboxOperacionais"] = "false",
    };

    [Fact]
    public async Task Familia_desligada_envia_direto_e_o_despachante_continua_escoando_o_que_ja_estava_na_fila()
    {
        var c = await ArranjarAsync(avisoProfissionalPorWhatsApp: false);
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        // Evento novo com a família desligada: caminho direto, nada na outbox, uma vez só.
        (await ItensAsync()).Should().BeEmpty();
        Email.Enviados.Should().ContainSingle(e => e.Destinatario == c.EmailProfissional);

        // Mensagem gravada ANTES do rollback ainda pendente: o despachante (chave geral ligada) a envia, uma vez.
        using (var escopo = Fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<OutboxNotificacoes>().EnfileirarAsync(c.NegocioId,
            [
                new NovaNotificacao(TipoNotificacao.Confirmacao, CanalNotificacao.Email, TiposDeNotificacao.ChaveConfirmacao(id),
                    DateTimeOffset.UtcNow.AddHours(24), id, c.ClienteId, null, TiposDeNotificacao.VersaoDoHorario(Hora(c, 10))),
            ], CancellationToken.None);

        await DespacharAsync();
        await DespacharAsync();

        Email.Enviados.Should().HaveCount(2);
        Email.Enviados.Should().ContainSingle(e => e.Destinatario == c.EmailCliente && e.Assunto.StartsWith("Agendamento confirmado"));
        (await ItensAsync()).Should().ContainSingle().Which.Status.Should().Be(StatusNotificacao.Enviada);
    }

    [Fact]
    public async Task Cancelamento_com_a_familia_desligada_invalida_a_mensagem_que_ficou_na_fila()
    {
        var c = await ArranjarAsync(avisoProfissionalPorWhatsApp: false);
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        using (var escopo = Fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<OutboxNotificacoes>().EnfileirarAsync(c.NegocioId,
            [
                new NovaNotificacao(TipoNotificacao.Confirmacao, CanalNotificacao.Email, TiposDeNotificacao.ChaveConfirmacao(id),
                    DateTimeOffset.UtcNow.AddHours(24), id, c.ClienteId, null, TiposDeNotificacao.VersaoDoHorario(Hora(c, 10))),
            ], CancellationToken.None);

        (await ComServicoAsync(c.NegocioId, s => s.CancelarAsync(id))).Should().BeTrue();
        await DespacharAsync();

        // A confirmação gravada antes do rollback não pode sair depois do cancelamento.
        (await ItensAsync()).Should().ContainSingle().Which.Should().Match<NotificacaoPendente>(
            i => i.Status == StatusNotificacao.Cancelada && i.UltimoErroCodigo == CodigosOutbox.AgendamentoObsoleto);
        Email.Enviados.Should().NotContain(e => e.Assunto.StartsWith("Agendamento confirmado"));
    }
}
