using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Clientes;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Notificacoes.Outbox;

/// <summary>
/// B7.4-B: o lembrete de retorno pela outbox. O job decide e grava (registro de relatório + mensagem, atomicamente); o despachante envia,
/// repete e conclui, e é o único mecanismo de nova tentativa. "Agora" do job é fixo (13:05 UTC, depois das 10h de São Paulo).
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class OutboxRetornoTestes(PostgresContainerFixture postgres) : OutboxTestesBase(postgres)
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 14, 13, 5, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>Liga o lembrete (30 dias, 10h) e semeia o último atendimento concluído do cliente há exatamente 30 dias.</summary>
    private async Task PrepararAsync(CenarioOutbox c, string mensagem = "Oi {nome_cliente}! {nome_empresa} sente sua falta há {dias} dias: {link_agendamento}")
    {
        await Fabrica.NoBancoAsync(async db =>
        {
            var negocio = await db.Negocios.IgnoreQueryFilters().SingleAsync(n => n.Id == c.NegocioId);
            negocio.ConfigurarLembreteRetorno(true, 30, new TimeOnly(10, 0), mensagem, Agora.AddDays(-90));

            var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Agora, SaoPaulo).DateTime);
            var atendimento = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(hoje.AddDays(-30).ToDateTime(new TimeOnly(14, 0)), SaoPaulo), TimeSpan.Zero);
            var agendamento = Agendamento.CriarConfirmado(c.NegocioId, Guid.NewGuid(), c.ClienteId, atendimento,
                [new ItemServicoAgendamento(c.ServicoId, "Corte", 50m, 30)]);
            agendamento.MarcarConcluido(0m, atendimento);
            db.Agendamentos.Add(agendamento);
            await db.SaveChangesAsync();
            return 0;
        });
    }

    private async Task RodarJobAsync()
    {
        using var escopo = Fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<JobLembreteRetorno>().ExecutarAsync(Agora);
    }

    private Task<List<LembreteRetornoEnvio>> EnviosAsync() =>
        Fabrica.NoBancoAsync(db => db.LembretesRetornoEnvios.IgnoreQueryFilters().AsNoTracking().ToListAsync());

    private Task<List<NotificacaoPendente>> ItensRetornoAsync() => ItensAsync(q => q.Where(n => n.Tipo == TipoNotificacao.LembreteRetorno));

    // ------------------------------------------------------------------ 36 criação idempotente

    [Fact]
    public async Task Job_cria_o_registro_e_a_mensagem_uma_vez_sem_enviar_pela_rede()
    {
        var c = await ArranjarAsync();
        await PrepararAsync(c);

        await RodarJobAsync();
        await RodarJobAsync();
        await RodarJobAsync();

        var itens = await ItensRetornoAsync();
        itens.Should().ContainSingle();
        itens[0].Canal.Should().Be(CanalNotificacao.WhatsApp);
        itens[0].ClienteId.Should().Be(c.ClienteId);
        itens[0].ChaveDeduplicacao.Should().Be(TiposDeNotificacao.ChaveRetorno(c.ClienteId, itens[0].AgendamentoId!.Value));
        (await EnviosAsync()).Should().ContainSingle().Which.Status.Should().Be(StatusLembreteRetorno.Enviando, "enfileirado: reservado, ainda não concluído");
        WhatsApp.Chamadas.Should().Be(0);

        await DespacharAsync();

        var texto = WhatsApp.Enviados.Should().ContainSingle().Subject.Mensagem;
        texto.Should().Contain("30 dias");
        (await EnviosAsync()).Single().Status.Should().Be(StatusLembreteRetorno.Enviado);
        (await ItensRetornoAsync()).Single().Status.Should().Be(StatusNotificacao.Enviada);

        // Nem o job nem o despachante repetem depois de enviado.
        await RodarJobAsync();
        await DespacharAsync();
        WhatsApp.Chamadas.Should().Be(1);
    }

    // ------------------------------------------------------------------ 37 falha retentável (único mecanismo de retry)

    [Fact]
    public async Task Falha_retentavel_e_repetida_pela_outbox_ate_esgotar_e_o_registro_acompanha()
    {
        var c = await ArranjarAsync();
        await PrepararAsync(c);
        WhatsApp.Resultado = ResultadoEnvioWhatsApp.Indisponivel;
        await RodarJobAsync();

        var esperas = new[] { 1, 5, 15 };
        for (var tentativa = 1; tentativa <= 4; tentativa++)
        {
            await DespacharAsync();
            await RodarJobAsync(); // o job nunca cria uma segunda mensagem nem tenta por conta própria
            var item = (await ItensRetornoAsync()).Single();
            item.Tentativas.Should().Be(tentativa);
            if (tentativa < 4)
            {
                item.Status.Should().Be(StatusNotificacao.Pendente);
                (await EnviosAsync()).Single().Status.Should().Be(StatusLembreteRetorno.Enviando, "ainda em tentativas");
                Relogio.Avancar(TimeSpan.FromMinutes(esperas[tentativa - 1]) + TimeSpan.FromSeconds(5));
            }
        }

        (await ItensRetornoAsync()).Single().Status.Should().Be(StatusNotificacao.Falhou);
        var envio = (await EnviosAsync()).Single();
        envio.Status.Should().Be(StatusLembreteRetorno.Falhou);
        envio.Erro.Should().Be(CodigosOutbox.TentativasEsgotadas);
        WhatsApp.Chamadas.Should().Be(4);
    }

    // ------------------------------------------------------------------ 38–39 ambíguo

    [Fact]
    public async Task Timeout_ambiguo_vira_desconhecida_no_item_e_no_registro_e_nunca_reenvia()
    {
        var c = await ArranjarAsync();
        await PrepararAsync(c);
        WhatsApp.Resultado = ResultadoEnvioWhatsApp.Ambigua;
        await RodarJobAsync();

        await DespacharAsync();

        (await ItensRetornoAsync()).Single().Status.Should().Be(StatusNotificacao.Desconhecida);
        var envio = (await EnviosAsync()).Single();
        envio.Status.Should().Be(StatusLembreteRetorno.Desconhecido);
        envio.Erro.Should().Be(CodigosOutbox.TimeoutAmbiguo);

        WhatsApp.Restaurar();
        Relogio.Avancar(TimeSpan.FromHours(2));
        await RodarJobAsync();
        await DespacharAsync();
        WhatsApp.Chamadas.Should().Be(1, "Desconhecida não é reenviada, nem pelo despachante nem pelo job");
        WhatsApp.Enviados.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ 40 lease preso

    [Fact]
    public async Task Registro_antigo_preso_em_enviando_sem_mensagem_viva_e_identificado_como_desconhecido()
    {
        var c = await ArranjarAsync();
        await PrepararAsync(c);
        // O caminho direto antigo reservou o registro e o processo caiu: Enviando há horas, sem nenhuma mensagem na outbox.
        await Fabrica.NoBancoAsync(async db =>
        {
            var atendimento = await db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.ClienteId == c.ClienteId);
            var envio = LembreteRetornoEnvio.Iniciar(c.NegocioId, c.ClienteId, atendimento.Id, DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow.AddHours(-3));
            db.LembretesRetornoEnvios.Add(envio);
            await db.SaveChangesAsync();
            return 0;
        });

        await DespacharAsync();

        var preso = (await EnviosAsync()).Single();
        preso.Status.Should().Be(StatusLembreteRetorno.Desconhecido);
        preso.Erro.Should().Be(CodigosOutbox.LeaseExpiradoAposInicio);

        await RodarJobAsync();
        await DespacharAsync();
        WhatsApp.Chamadas.Should().Be(0, "o registro existente impede qualquer reenvio");
    }

    [Fact]
    public async Task Registro_enviando_com_mensagem_viva_na_outbox_nao_e_tocado()
    {
        var c = await ArranjarAsync();
        await PrepararAsync(c);
        WhatsApp.Resultado = ResultadoEnvioWhatsApp.Indisponivel;
        await RodarJobAsync();
        await DespacharAsync(); // fica Pendente aguardando a nova tentativa

        Relogio.Avancar(TimeSpan.FromMinutes(70)); // passa do limite de "preso", mas a mensagem ainda vive (validade de 3 h)
        WhatsApp.Restaurar();
        await DespacharAsync();

        (await EnviosAsync()).Single().Status.Should().Be(StatusLembreteRetorno.Enviado);
    }

    [Fact]
    public async Task Queda_depois_de_iniciar_o_envio_do_retorno_deixa_item_e_registro_desconhecidos()
    {
        var c = await ArranjarAsync();
        await PrepararAsync(c);
        await RodarJobAsync();
        var inicio = Relogio.GetUtcNow().AddMinutes(-5).ToUniversalTime();
        var lease = Relogio.GetUtcNow().AddMinutes(-3).ToUniversalTime();
        await SqlAsync($@"UPDATE notificacoes_pendentes
                             SET status = 'Enviando', tentativas = 1, envio_iniciado_em = {inicio}, lease_ate = {lease}, reivindicacao_id = {Guid.NewGuid()}
                           WHERE tipo = 'LembreteRetorno'");

        await DespacharAsync();

        (await ItensRetornoAsync()).Single().Status.Should().Be(StatusNotificacao.Desconhecida);
        (await EnviosAsync()).Single().Status.Should().Be(StatusLembreteRetorno.Desconhecido);
        WhatsApp.Chamadas.Should().Be(0);
    }

    // ------------------------------------------------------------------ 41 opt-out e demais verificações no despacho

    [Fact]
    public async Task Opt_out_antes_do_despacho_cancela_a_mensagem_e_fecha_o_registro()
    {
        var c = await ArranjarAsync();
        await PrepararAsync(c);
        await RodarJobAsync();

        await SqlAsync($"UPDATE clientes SET aceita_lembrete_retorno = false WHERE id = {c.ClienteId}");
        await DespacharAsync();

        var item = (await ItensRetornoAsync()).Single();
        item.Status.Should().Be(StatusNotificacao.Cancelada);
        item.UltimoErroCodigo.Should().Be(CodigosOutbox.OptOut);
        WhatsApp.Chamadas.Should().Be(0);
        var envio = (await EnviosAsync()).Single();
        envio.Status.Should().Be(StatusLembreteRetorno.Falhou);
        envio.Erro.Should().Be(CodigosOutbox.OptOut);
    }

    [Fact]
    public async Task Negocio_que_desliga_o_lembrete_antes_do_despacho_cancela_a_mensagem()
    {
        var c = await ArranjarAsync();
        await PrepararAsync(c);
        await RodarJobAsync();

        await SqlAsync($"UPDATE negocios SET lembrete_retorno_ativo = false WHERE id = {c.NegocioId}");
        await DespacharAsync();

        (await ItensRetornoAsync()).Single().UltimoErroCodigo.Should().Be(CodigosOutbox.CanalDesativado);
        WhatsApp.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Cliente_que_marcou_um_horario_antes_do_despacho_nao_recebe_o_lembrete()
    {
        var c = await ArranjarAsync();
        await PrepararAsync(c);
        await RodarJobAsync();

        await Fabrica.NoBancoAsync(async db =>
        {
            db.Agendamentos.Add(Agendamento.CriarConfirmado(c.NegocioId, Guid.NewGuid(), c.ClienteId, DateTimeOffset.UtcNow.AddDays(2),
                [new ItemServicoAgendamento(c.ServicoId, "Corte", 50m, 30)]));
            await db.SaveChangesAsync();
            return 0;
        });
        await DespacharAsync();

        (await ItensRetornoAsync()).Single().UltimoErroCodigo.Should().Be(CodigosOutbox.ClienteInelegivel);
        WhatsApp.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Lembrete_de_retorno_fora_da_validade_e_cancelado_sem_envio()
    {
        var c = await ArranjarAsync();
        await PrepararAsync(c);
        await RodarJobAsync();

        Relogio.Avancar(TimeSpan.FromHours(4)); // passa dos 180 minutos de validade
        await DespacharAsync();

        var item = (await ItensRetornoAsync()).Single();
        item.Status.Should().Be(StatusNotificacao.Cancelada);
        item.UltimoErroCodigo.Should().Be(CodigosOutbox.Expirada);
        WhatsApp.Chamadas.Should().Be(0);
        (await EnviosAsync()).Single().Status.Should().Be(StatusLembreteRetorno.Falhou);
    }

    // ------------------------------------------------------------------ 42 isolamento

    [Fact]
    public async Task Dois_negocios_nao_misturam_mensagem_registro_nem_item()
    {
        var a = await ArranjarAsync();
        var b = await ArranjarAsync();
        await PrepararAsync(a, "ALFA {nome_cliente} {nome_empresa} {dias} dias {link_agendamento}");
        await PrepararAsync(b, "BETA {nome_cliente} {nome_empresa} {dias} dias {link_agendamento}");
        await SqlAsync($"UPDATE negocios SET nome_exibido = 'Barbearia Alfa' WHERE id = {a.NegocioId}");
        await SqlAsync($"UPDATE negocios SET nome_exibido = 'Studio Beta' WHERE id = {b.NegocioId}");

        await RodarJobAsync();
        var itens = await ItensRetornoAsync();
        itens.Should().HaveCount(2);
        itens.Select(i => i.NegocioId).Should().BeEquivalentTo([a.NegocioId, b.NegocioId]);

        await DespacharAsync();

        WhatsApp.Enviados.Should().HaveCount(2);
        var mensagens = WhatsApp.Enviados.Select(e => e.Mensagem).ToList();
        mensagens.Should().ContainSingle(m => m.StartsWith("ALFA") && m.Contains("Barbearia Alfa") && !m.Contains("Beta"));
        mensagens.Should().ContainSingle(m => m.StartsWith("BETA") && m.Contains("Studio Beta") && !m.Contains("Alfa"));
        var envios = await EnviosAsync();
        envios.Should().HaveCount(2).And.OnlyContain(e => e.Status == StatusLembreteRetorno.Enviado);
        envios.Select(e => e.NegocioId).Should().BeEquivalentTo([a.NegocioId, b.NegocioId]);
    }
}
