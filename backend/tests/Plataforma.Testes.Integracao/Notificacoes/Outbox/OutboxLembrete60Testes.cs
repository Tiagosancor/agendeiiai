using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Notificacoes.Outbox;

/// <summary>
/// B7.4-B: o lembrete de 60 minutos pela outbox. O job deixa de enviar: grava a mensagem (por canal, idempotente, com o horário
/// versionado) na mesma transação que marca o lembrete, e o despachante envia. Remarcação e cancelamento invalidam o que esperava.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class OutboxLembrete60Testes(PostgresContainerFixture postgres) : OutboxTestesBase(postgres)
{
    private static readonly TimeSpan Antecedencia = TimeSpan.FromMinutes(60);

    private void RelogioEm(DateTimeOffset instante) => Relogio.Deslocamento = instante - DateTimeOffset.UtcNow;

    private async Task RodarProcessadorAsync(Guid negocioId)
    {
        using var escopo = Fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<ProcessadorLembretesDoNegocio>().ProcessarAsync(negocioId, Relogio.GetUtcNow());
    }

    private async Task RodarJobAsync()
    {
        using var escopo = Fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<JobEnviarLembretes>().ExecutarAsync();
    }

    /// <summary>Agendamento confirmado direto no banco, marcado "ontem" (o lembrete faz sentido), começando daqui a <paramref name="inicioEm"/>.</summary>
    private Task<Guid> SemearNaJanelaAsync(CenarioOutbox c, TimeSpan inicioEm, Guid? profissionalId = null) =>
        Fabrica.NoBancoAsync(async db =>
        {
            var itens = new[] { new ItemServicoAgendamento(c.ServicoId, "Corte", 50m, 30) };
            var agendamento = Agendamento.CriarConfirmado(c.NegocioId, profissionalId ?? c.ProfissionalId, c.ClienteId, DateTimeOffset.UtcNow + inicioEm, itens);
            typeof(Agendamento).GetProperty(nameof(Agendamento.HorarioCombinadoEm))!.SetValue(agendamento, DateTimeOffset.UtcNow.AddDays(-1));
            db.Agendamentos.Add(agendamento);
            await db.SaveChangesAsync();
            return agendamento.Id;
        });

    private Task<bool> MarcadoAsync(Guid agendamentoId) =>
        Fabrica.NoBancoAsync(async db => (await db.Agendamentos.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == agendamentoId)).LembreteEnviado);

    private static List<NotificacaoPendente> Lembretes(IEnumerable<NotificacaoPendente> itens) =>
        itens.Where(i => i.Tipo == TipoNotificacao.Lembrete60).ToList();

    // ------------------------------------------------------------------ 19, 25, 27

    [Fact]
    public async Task Elegibilidade_enfileira_o_lembrete_uma_vez_por_canal_sem_enviar_direto()
    {
        var c = await ArranjarAsync();
        var id = await SemearNaJanelaAsync(c, TimeSpan.FromMinutes(45));

        await RodarJobAsync();
        await RodarJobAsync();
        await RodarJobAsync();

        var itens = Lembretes(await ItensDoAgendamentoAsync(id));
        itens.Should().HaveCount(2, "um item por canal, e a execução recorrente não duplica");
        itens.Select(i => i.Canal).Should().BeEquivalentTo([CanalNotificacao.Email, CanalNotificacao.WhatsApp]);
        itens.Should().OnlyContain(i => i.Status == StatusNotificacao.Pendente && i.NegocioId == c.NegocioId && i.ClienteId == c.ClienteId);
        Email.Chamadas.Should().Be(0);
        WhatsApp.Chamadas.Should().Be(0);

        var agendamento = await Fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == id));
        itens.Should().OnlyContain(i => i.VersaoHorario == TiposDeNotificacao.VersaoDoHorario(agendamento.Inicio));
        itens.Should().OnlyContain(i => i.ExpiraEm == agendamento.Inicio.AddMinutes(-30), "vence 30 min antes do atendimento");
        (await MarcadoAsync(id)).Should().BeTrue("o campo legado passa a significar 'enfileirado'");

        await DespacharAsync();
        Email.Enviados.Should().ContainSingle(e => e.Destinatario == c.EmailCliente && e.Assunto.StartsWith("Lembrete"));
        WhatsApp.Enviados.Should().ContainSingle(m => m.Mensagem.StartsWith("Lembrete"));

        await RodarJobAsync();
        await DespacharAsync();
        Email.Enviados.Should().ContainSingle("depois de enfileirado e enviado, nada se repete");
    }

    // ------------------------------------------------------------------ 20–21 vários negócios e instâncias

    [Fact]
    public async Task Dois_negocios_na_mesma_janela_enfileiram_um_lembrete_cada_sem_misturar()
    {
        var a = await ArranjarAsync();
        var b = await ArranjarAsync();
        var idA = await SemearNaJanelaAsync(a, TimeSpan.FromMinutes(45));
        var idB = await SemearNaJanelaAsync(b, TimeSpan.FromMinutes(50));

        await RodarJobAsync();

        var itens = await ItensAsync();
        itens.Where(i => i.NegocioId == a.NegocioId).Should().HaveCount(2).And.OnlyContain(i => i.AgendamentoId == idA);
        itens.Where(i => i.NegocioId == b.NegocioId).Should().HaveCount(2).And.OnlyContain(i => i.AgendamentoId == idB);

        await DespacharAsync();

        Email.Enviados.Should().HaveCount(2);
        Email.Enviados.Should().ContainSingle(e => e.Destinatario == a.EmailCliente);
        Email.Enviados.Should().ContainSingle(e => e.Destinatario == b.EmailCliente);
    }

    [Fact]
    public async Task Duas_instancias_do_job_ao_mesmo_tempo_nao_criam_dois_itens()
    {
        var c = await ArranjarAsync();
        var id = await SemearNaJanelaAsync(c, TimeSpan.FromMinutes(45));

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(RodarJobAsync)));

        Lembretes(await ItensDoAgendamentoAsync(id)).Should().HaveCount(2);
        await DespacharAsync();
        Email.Enviados.Should().ContainSingle();
    }

    // ------------------------------------------------------------------ 22–23 remarcação e cancelamento

    [Fact]
    public async Task Remarcacao_cancela_o_lembrete_antigo_e_o_horario_novo_gera_outro()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        RelogioEm(Hora(c, 10) - TimeSpan.FromMinutes(45));
        await RodarProcessadorAsync(c.NegocioId);
        var antigos = Lembretes(await ItensDoAgendamentoAsync(id));
        antigos.Should().HaveCount(2).And.OnlyContain(i => i.Status == StatusNotificacao.Pendente);

        // Remarca para as 11h antes do despacho: o lembrete das 10h deixa de valer e o campo legado volta a "não enviado".
        Relogio.Deslocamento = TimeSpan.Zero;
        (await ComServicoAsync(c.NegocioId, s => s.MoverAsync(id, Hora(c, 11)))).Sucesso.Should().BeTrue();

        Lembretes(await ItensDoAgendamentoAsync(id)).Should().OnlyContain(
            i => i.Status == StatusNotificacao.Cancelada && i.UltimoErroCodigo == CodigosOutbox.HorarioAlterado);
        (await MarcadoAsync(id)).Should().BeFalse();

        RelogioEm(Hora(c, 11) - TimeSpan.FromMinutes(45));
        await RodarProcessadorAsync(c.NegocioId);

        var novos = Lembretes(await ItensDoAgendamentoAsync(id)).Where(i => i.Status == StatusNotificacao.Pendente).ToList();
        novos.Should().HaveCount(2);
        novos.Should().OnlyContain(i => i.VersaoHorario == TiposDeNotificacao.VersaoDoHorario(Hora(c, 11)));

        await DespacharAsync();
        var lembretes = Email.Enviados.Where(e => e.Assunto.StartsWith("Lembrete")).ToList();
        lembretes.Should().ContainSingle("só o do horário vigente");
        lembretes[0].CorpoHtml.Should().Contain("11:00").And.NotContain("10:00");
    }

    [Fact]
    public async Task Lembrete_antigo_ja_reivindicado_confere_o_horario_antes_de_enviar()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        RelogioEm(Hora(c, 10) - TimeSpan.FromMinutes(45));
        await RodarProcessadorAsync(c.NegocioId);

        // O horário muda por fora do serviço (sem nenhuma invalidação explícita): a versão guardada na mensagem não bate mais.
        await SqlAsync($"UPDATE agendamentos SET inicio = {Hora(c, 11).ToUniversalTime()}, fim = {Hora(c, 11).AddMinutes(30).ToUniversalTime()} WHERE id = {id}");
        await DespacharAsync();

        Email.Enviados.Should().NotContain(e => e.Assunto.StartsWith("Lembrete"));
        Lembretes(await ItensDoAgendamentoAsync(id)).Should().OnlyContain(
            i => i.Status == StatusNotificacao.Cancelada && i.UltimoErroCodigo == CodigosOutbox.HorarioAlterado);
    }

    [Fact]
    public async Task Cancelamento_cancela_o_lembrete_pendente()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        RelogioEm(Hora(c, 10) - TimeSpan.FromMinutes(45));
        await RodarProcessadorAsync(c.NegocioId);

        Relogio.Deslocamento = TimeSpan.Zero;
        (await ComServicoAsync(c.NegocioId, s => s.CancelarAsync(id))).Should().BeTrue();

        Lembretes(await ItensDoAgendamentoAsync(id)).Should().OnlyContain(
            i => i.Status == StatusNotificacao.Cancelada && i.UltimoErroCodigo == CodigosOutbox.AgendamentoObsoleto);
        await DespacharAsync();
        Email.Enviados.Should().NotContain(e => e.Assunto.StartsWith("Lembrete"));
    }

    // ------------------------------------------------------------------ 24 menos de 30 minutos

    [Fact]
    public async Task Lembrete_que_so_seria_enviado_a_menos_de_30_minutos_do_atendimento_termina_cancelado_sem_envio()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        RelogioEm(Hora(c, 10) - TimeSpan.FromMinutes(20));

        await RodarProcessadorAsync(c.NegocioId);
        await DespacharAsync();

        var itens = Lembretes(await ItensDoAgendamentoAsync(id));
        itens.Should().HaveCount(2).And.OnlyContain(i => i.Status == StatusNotificacao.Cancelada && i.UltimoErroCodigo == CodigosOutbox.Expirada);
        Email.Enviados.Should().NotContain(e => e.Assunto.StartsWith("Lembrete"));
        WhatsApp.Enviados.Should().NotContain(m => m.Mensagem.StartsWith("Lembrete"));
    }

    [Fact]
    public async Task Retry_do_lembrete_que_passaria_do_limite_de_30_minutos_e_cancelado_em_vez_de_enviado_tarde()
    {
        var c = await ArranjarAsync(whatsApp: false);
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        RelogioEm(Hora(c, 10) - TimeSpan.FromMinutes(45));
        await DespacharAsync(); // os avisos ao profissional saem antes; só o lembrete falha abaixo
        await RodarProcessadorAsync(c.NegocioId);
        Email.Falhar = true;
        var chamadasAntes = Email.Chamadas;

        // Falha em T−45 (próxima em 1 min), T−44 (5 min) e T−39: a próxima, em 15 min, seria T−24 — depois do limite de T−30.
        await DespacharAsync();
        Relogio.Avancar(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(5));
        await DespacharAsync();
        Relogio.Avancar(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(5));
        await DespacharAsync();

        Email.Falhar = false;
        Relogio.Avancar(TimeSpan.FromMinutes(20));
        await DespacharAsync();

        (Email.Chamadas - chamadasAntes).Should().Be(3, "a 4ª tentativa cairia depois do limite e não existe");
        Email.Enviados.Should().NotContain(e => e.Assunto.StartsWith("Lembrete"));
        var item = Lembretes(await ItensDoAgendamentoAsync(id)).Single();
        item.Status.Should().Be(StatusNotificacao.Cancelada);
        item.UltimoErroCodigo.Should().Be(CodigosOutbox.Expirada);
    }

    // ------------------------------------------------------------------ limites de tentativas do lembrete

    [Fact]
    public async Task Lembrete_tem_despacho_imediato_mais_tres_novas_tentativas_apos_1_5_e_15_minutos()
    {
        var c = await ArranjarAsync(whatsApp: false);
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        RelogioEm(Hora(c, 10) - TimeSpan.FromMinutes(58));
        await DespacharAsync(); // avisos ao profissional
        await RodarProcessadorAsync(c.NegocioId);
        Email.Falhar = true;
        var chamadasAntes = Email.Chamadas;
        var esperas = new[] { 1, 5, 15 };

        for (var tentativa = 1; tentativa <= 4; tentativa++)
        {
            await DespacharAsync();
            var item = Lembretes(await ItensDoAgendamentoAsync(id)).Single();
            item.Tentativas.Should().Be(tentativa);

            if (tentativa == 4)
            {
                item.Status.Should().Be(StatusNotificacao.Falhou);
                item.UltimoErroCodigo.Should().Be(CodigosOutbox.TentativasEsgotadas);
                break;
            }

            item.Status.Should().Be(StatusNotificacao.Pendente);
            (item.ProximaTentativaEm - Relogio.GetUtcNow()).Should().BeCloseTo(TimeSpan.FromMinutes(esperas[tentativa - 1]), TimeSpan.FromSeconds(10));
            Relogio.Avancar(TimeSpan.FromMinutes(esperas[tentativa - 1]) + TimeSpan.FromSeconds(5));
        }

        (Email.Chamadas - chamadasAntes).Should().Be(4);
    }

    // ------------------------------------------------------------------ 26 fuso

    [Fact]
    public async Task Lembrete_mostra_o_horario_no_fuso_do_negocio()
    {
        var c = await ArranjarAsync();
        await SqlAsync($"UPDATE negocios SET fuso = 'America/Manaus' WHERE id = {c.NegocioId}");
        var inicio = DateTimeOffset.UtcNow.AddMinutes(45);
        var id = await SemearNaJanelaAsync(c, TimeSpan.FromMinutes(45));
        var inicioReal = await Fabrica.NoBancoAsync(async db => (await db.Agendamentos.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == id)).Inicio);

        await RodarJobAsync();
        await DespacharAsync();

        var email = Email.Enviados.Single(e => e.Assunto.StartsWith("Lembrete"));
        email.CorpoHtml.Should().Contain(FormatacaoBrasil.DataHora(inicioReal, "America/Manaus"));
        inicio.Should().BeCloseTo(inicioReal, TimeSpan.FromMinutes(2));
        var manaus = TimeZoneInfo.ConvertTime(inicioReal, TimeZoneInfo.FindSystemTimeZoneById("America/Manaus"));
        var saoPaulo = TimeZoneInfo.ConvertTime(inicioReal, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        email.CorpoHtml.Should().Contain(manaus.ToString("HH:mm")).And.NotContain($"às {saoPaulo:HH:mm}");
    }
}

/// <summary>Rollout: só a família do lembrete desligada — o job envia direto, como sempre, e o campo legado segue o mesmo significado.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class OutboxLembreteFamiliaDesligadaTestes(PostgresContainerFixture postgres) : OutboxTestesBase(postgres)
{
    protected override Dictionary<string, string?> Configuracao => new()
    {
        ["Notificacoes:OutboxHabilitada"] = "true",
        ["Notificacoes:OutboxLembrete"] = "false",
        ["Notificacoes:OutboxOperacionais"] = "true",
    };

    [Fact]
    public async Task Com_a_familia_do_lembrete_desligada_o_job_envia_direto_e_marca_enviado_sem_tocar_a_outbox()
    {
        var c = await ArranjarAsync();
        var id = await Fabrica.NoBancoAsync(async db =>
        {
            var itens = new[] { new ItemServicoAgendamento(c.ServicoId, "Corte", 50m, 30) };
            var agendamento = Agendamento.CriarConfirmado(c.NegocioId, c.ProfissionalId, c.ClienteId, DateTimeOffset.UtcNow.AddMinutes(45), itens);
            typeof(Agendamento).GetProperty(nameof(Agendamento.HorarioCombinadoEm))!.SetValue(agendamento, DateTimeOffset.UtcNow.AddDays(-1));
            db.Agendamentos.Add(agendamento);
            await db.SaveChangesAsync();
            return agendamento.Id;
        });

        using (var escopo = Fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<JobEnviarLembretes>().ExecutarAsync();

        Email.Enviados.Should().ContainSingle(e => e.Destinatario == c.EmailCliente && e.Assunto.StartsWith("Lembrete"));
        (await ItensDoAgendamentoAsync(id)).Should().BeEmpty();
        (await Fabrica.NoBancoAsync(async db => (await db.Agendamentos.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == id)).LembreteEnviado)).Should().BeTrue();
    }
}
