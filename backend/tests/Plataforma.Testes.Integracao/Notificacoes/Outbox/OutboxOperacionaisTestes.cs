using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.Profissionais;
using Plataforma.Infraestrutura.Notificacoes.Outbox;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Notificacoes.Outbox;

/// <summary>
/// B7.4-B: confirmação, cancelamento, remarcação, transferência e avisos ao profissional passam pela outbox — gravados na transação do
/// efeito, enviados (uma vez por canal) pelo despachante, e nunca enviados quando o estado do agendamento os tornou obsoletos.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class OutboxOperacionaisTestes(PostgresContainerFixture postgres) : OutboxTestesBase(postgres)
{
    // ------------------------------------------------------------------ 1–6 persistência e atomicidade

    [Fact]
    public async Task Agendamento_e_mensagens_da_outbox_sao_gravados_juntos_e_nada_sai_pela_rede_na_requisicao()
    {
        var c = await ArranjarAsync();

        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Should().HaveCount(2).And.OnlyContain(i => i.Tipo == TipoNotificacao.ProfissionalNovo && i.Status == StatusNotificacao.Pendente);
        itens.Select(i => i.Canal).Should().BeEquivalentTo([CanalNotificacao.Email, CanalNotificacao.WhatsApp]);
        itens.Should().OnlyContain(i => i.NegocioId == c.NegocioId && i.ProfissionalId == c.ProfissionalId);

        // A rede nunca é chamada dentro da transação nem na requisição: o envio é do despachante.
        Email.Chamadas.Should().Be(0);
        WhatsApp.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task Falha_antes_do_commit_desfaz_o_agendamento_e_as_mensagens()
    {
        var c = await ArranjarAsync();

        Commit.ArmarFalhaAntesDoCommit(vezes: 1, transitoria: false);
        var criar = () => ComServicoAsync(c.NegocioId, s => s.CriarAsync(new CriarAgendamento(c.ProfissionalId, c.ClienteId, [c.ServicoId], Hora(c, 10))));
        await criar.Should().ThrowAsync<Exception>();
        Commit.Falhas.Should().Be(1, "a falha simulada precisa ter acontecido, senão o teste não prova nada");

        (await Fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().CountAsync())).Should().Be(0);
        (await ItensAsync()).Should().BeEmpty("se a transação falhou, nada fica pendente");
    }

    [Fact]
    public async Task Commit_confirmado_com_resposta_perdida_nao_duplica_o_agendamento_nem_as_mensagens()
    {
        var c = await ArranjarAsync();

        Commit.ArmarCommitAmbiguo();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        Commit.Falhas.Should().Be(1);
        (await Fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().CountAsync())).Should().Be(1);
        var itens = await ItensAsync();
        itens.Should().HaveCount(2, "a repetição da estratégia reencontra as chaves e não cria outras");
        itens.Should().OnlyContain(i => i.AgendamentoId == id);
    }

    [Fact]
    public async Task Idempotency_key_repetida_reaproveita_o_efeito_e_a_mensagem()
    {
        var c = await ArranjarAsync();
        var pedido = new LancarEncaixe(c.ProfissionalId, c.ClienteId, null, [c.ServicoId], Hora(c, 10), IniciarAtendimento: false, ClienteAutorizouMensagens: true);
        var chave = Guid.NewGuid().ToString();

        async Task<HttpResponseMessage> EnviarAsync()
        {
            using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/painel/encaixes") { Content = JsonContent.Create(pedido) };
            requisicao.Headers.Add("Idempotency-Key", chave);
            return await c.Admin.SendAsync(requisicao);
        }

        var primeira = await EnviarAsync();
        var segunda = await EnviarAsync();

        primeira.StatusCode.Should().Be(HttpStatusCode.Created);
        segunda.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().CountAsync())).Should().Be(1);

        // Encaixe autorizado a receber mensagens: confirmação (2 canais) + aviso ao profissional (2 canais), uma vez só.
        var itens = await ItensAsync();
        itens.Should().HaveCount(4);
        itens.Count(i => i.Tipo == TipoNotificacao.Confirmacao).Should().Be(2);
        itens.Count(i => i.Tipo == TipoNotificacao.ProfissionalNovo).Should().Be(2);

        await DespacharAsync();
        await DespacharAsync();
        Email.Enviados.Should().HaveCount(2, "um e-mail ao cliente e um ao profissional");
        WhatsApp.Enviados.Should().HaveCount(2);
    }

    [Fact]
    public async Task Mesma_chave_tecnica_em_negocios_diferentes_nao_colide_e_no_mesmo_negocio_nao_duplica()
    {
        var a = await ArranjarAsync();
        var b = await ArranjarAsync();
        var agendamentoId = Guid.NewGuid();
        var chave = TiposDeNotificacao.ChaveConfirmacao(agendamentoId);
        var expira = DateTimeOffset.UtcNow.AddHours(1);

        async Task<int> EnfileirarAsync(Guid negocioId)
        {
            using var escopo = Fabrica.Services.CreateScope();
            return await escopo.ServiceProvider.GetRequiredService<OutboxNotificacoes>().EnfileirarAsync(negocioId,
                [new NovaNotificacao(TipoNotificacao.Confirmacao, CanalNotificacao.Email, chave, expira, agendamentoId)], CancellationToken.None);
        }

        (await EnfileirarAsync(a.NegocioId)).Should().Be(1);
        (await EnfileirarAsync(b.NegocioId)).Should().Be(1, "o mesmo identificador técnico em outro tenant é outro item");
        (await EnfileirarAsync(a.NegocioId)).Should().Be(0, "no mesmo tenant e canal a chave é única");

        (await ItensAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Conflito_de_horario_nao_deixa_mensagem_pendente()
    {
        var c = await ArranjarAsync();
        await CriarAgendamentoAsync(c, Hora(c, 10));

        var conflito = await ComServicoAsync(c.NegocioId, s => s.CriarAsync(
            new CriarAgendamento(c.ProfissionalId, c.ClienteId, [c.ServicoId], Hora(c, 10))));

        conflito.Sucesso.Should().BeFalse();
        (await ItensAsync()).Should().HaveCount(2, "só as mensagens do agendamento que de fato foi gravado");
    }

    // ------------------------------------------------------------------ 28–32 cada notificação operacional

    [Fact]
    public async Task Confirmacao_publica_enfileira_confirmacao_ao_cliente_e_aviso_ao_profissional_em_cada_canal()
    {
        var c = await ArranjarAsync();
        var reserva = await ComServicoAsync(c.NegocioId, s => s.CriarReservaPublicaAsync(new CriarReservaPublica(c.ProfissionalId, [c.ServicoId], Hora(c, 10))));
        reserva.Sucesso.Should().BeTrue();

        var confirmada = await ComServicoAsync(c.NegocioId, s => s.ConfirmarReservaPublicaAsync(new ConfirmarReservaPublica(
            reserva.AgendamentoId!.Value, "Cliente Novo", "+5571977776666", "novo.cliente@teste.com", null, null, "127.0.0.1")));
        confirmada.Sucesso.Should().BeTrue();

        var itens = await ItensDoAgendamentoAsync(reserva.AgendamentoId!.Value);
        itens.Should().HaveCount(4);
        itens.Count(i => i.Tipo == TipoNotificacao.Confirmacao).Should().Be(2);
        itens.Count(i => i.Tipo == TipoNotificacao.ProfissionalNovo).Should().Be(2);
        Email.Chamadas.Should().Be(0);

        await DespacharAsync();

        Email.Enviados.Should().Contain(e => e.Destinatario == "novo.cliente@teste.com" && e.Assunto.StartsWith("Agendamento confirmado"));
        Email.Enviados.Should().Contain(e => e.Destinatario == c.EmailProfissional && e.Assunto.StartsWith("Novo agendamento"));
        WhatsApp.Enviados.Should().Contain(e => e.Telefone.Valor == "+5571977776666" && e.Mensagem.StartsWith("Agendamento confirmado"));
        (await ItensAsync()).Should().OnlyContain(i => i.Status == StatusNotificacao.Enviada);
    }

    [Fact]
    public async Task Cancelamento_avisando_o_cliente_enfileira_o_aviso_e_invalida_o_que_estava_obsoleto()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        (await ComServicoAsync(c.NegocioId, s => s.CancelarAvisandoClienteAsync(id))).Should().BeTrue();

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Where(i => i.Tipo == TipoNotificacao.ProfissionalNovo).Should().OnlyContain(
            i => i.Status == StatusNotificacao.Cancelada && i.UltimoErroCodigo == CodigosOutbox.AgendamentoObsoleto,
            "o 'novo agendamento' pendente ficou obsoleto");
        itens.Count(i => i.Tipo == TipoNotificacao.Cancelamento && i.Status == StatusNotificacao.Pendente).Should().Be(2);
        itens.Count(i => i.Tipo == TipoNotificacao.ProfissionalCancelado && i.Status == StatusNotificacao.Pendente).Should().Be(2);

        await DespacharAsync();

        Email.Enviados.Should().Contain(e => e.Destinatario == c.EmailCliente && e.Assunto.StartsWith("Agendamento cancelado"));
        Email.Enviados.Should().Contain(e => e.Destinatario == c.EmailProfissional && e.Assunto.StartsWith("Agendamento cancelado"));
        Email.Enviados.Should().NotContain(e => e.Assunto.StartsWith("Novo agendamento"), "nunca envia o que ficou obsoleto");
        WhatsApp.Enviados.Should().HaveCount(2).And.OnlyContain(m => m.Mensagem.Contains("cancel"));
    }

    [Fact]
    public async Task Cancelamento_sem_avisar_o_cliente_avisa_so_o_profissional()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        (await ComServicoAsync(c.NegocioId, s => s.CancelarAsync(id))).Should().BeTrue();

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Should().NotContain(i => i.Tipo == TipoNotificacao.Cancelamento);
        itens.Count(i => i.Tipo == TipoNotificacao.ProfissionalCancelado).Should().Be(2);
    }

    [Fact]
    public async Task Remarcacao_enfileira_o_aviso_do_horario_novo_com_versao_propria()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        (await ComServicoAsync(c.NegocioId, s => s.MoverAsync(id, Hora(c, 11)))).Sucesso.Should().BeTrue();
        (await ComServicoAsync(c.NegocioId, s => s.MoverAsync(id, Hora(c, 14)))).Sucesso.Should().BeTrue();

        var remarcados = (await ItensDoAgendamentoAsync(id)).Where(i => i.Tipo == TipoNotificacao.ProfissionalRemarcado).ToList();
        remarcados.Should().HaveCount(4, "duas remarcações × dois canais, uma chave por horário novo");
        remarcados.Select(i => i.VersaoHorario).Distinct().Should().BeEquivalentTo(
            [TiposDeNotificacao.VersaoDoHorario(Hora(c, 11)), TiposDeNotificacao.VersaoDoHorario(Hora(c, 14))]);

        await DespacharAsync();

        // A remarcação do 11h ficou obsoleta (já foi remarcado de novo): sai só a do horário vigente, e o "novo" mostra o horário atual.
        Email.Enviados.Count(e => e.Assunto.StartsWith("Agendamento remarcado")).Should().Be(1);
        var remarcado = Email.Enviados.Single(e => e.Assunto.StartsWith("Agendamento remarcado"));
        var itensFinais = await ItensDoAgendamentoAsync(id);
        itensFinais.Where(i => i.Tipo == TipoNotificacao.ProfissionalRemarcado && i.VersaoHorario == TiposDeNotificacao.VersaoDoHorario(Hora(c, 11)))
            .Should().OnlyContain(i => i.Status == StatusNotificacao.Cancelada && i.UltimoErroCodigo == CodigosOutbox.HorarioAlterado);
        remarcado.CorpoHtml.Should().Contain("14:00");
        remarcado.CorpoHtml.Should().NotContain("11:00");
    }

    [Fact]
    public async Task Transferencia_avisa_o_novo_profissional_e_nao_o_antigo()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        var (outroId, emailOutro) = await CriarOutroProfissionalAsync(c);

        (await ComServicoAsync(c.NegocioId, s => s.TransferirAsync(id, c.ProfissionalId, outroId))).Sucesso.Should().BeTrue();

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Count(i => i.Tipo == TipoNotificacao.ProfissionalNovo && i.ProfissionalId == outroId).Should().Be(2, "e-mail e WhatsApp do profissional novo");

        await DespacharAsync();

        Email.Enviados.Should().Contain(e => e.Destinatario == emailOutro && e.Assunto.StartsWith("Novo agendamento"));
        // O 'novo' do profissional antigo ficou obsoleto: o atendimento não é mais dele.
        Email.Enviados.Should().NotContain(e => e.Destinatario == c.EmailProfissional);
        (await ItensDoAgendamentoAsync(id)).Where(i => i.ProfissionalId == c.ProfissionalId)
            .Should().OnlyContain(i => i.Status == StatusNotificacao.Cancelada && i.UltimoErroCodigo == CodigosOutbox.AgendamentoObsoleto);
    }

    [Fact]
    public async Task Aviso_ao_profissional_sai_so_por_email_quando_o_negocio_nao_liga_o_whatsapp_do_aviso()
    {
        var c = await ArranjarAsync(avisoProfissionalPorWhatsApp: false);

        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Should().ContainSingle().Which.Canal.Should().Be(CanalNotificacao.Email);
        await DespacharAsync();
        WhatsApp.Chamadas.Should().Be(0);
        (await Fabrica.NoBancoAsync(db => db.NotificacoesProfissional.IgnoreQueryFilters().CountAsync())).Should().Be(1, "o registro por canal do aviso (onde o webhook grava)");
    }

    // ------------------------------------------------------------------ 33–35 queda, replay, obsolescência

    [Fact]
    public async Task Mensagem_gravada_antes_da_queda_do_processo_ainda_sai_depois()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        // "A API caiu depois do commit": a requisição acabou sem enviar nada. Uma instância nova (o despachante) encontra as mensagens.
        Email.Chamadas.Should().Be(0);
        (await ItensDoAgendamentoAsync(id)).Should().OnlyContain(i => i.Status == StatusNotificacao.Pendente);

        await DespacharAsync();

        Email.Enviados.Should().ContainSingle(e => e.Destinatario == c.EmailProfissional);
        WhatsApp.Enviados.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancelamento_com_commit_confirmado_e_resposta_perdida_nao_duplica_as_mensagens()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        // O servidor confirma o cancelamento e a resposta se perde: a estratégia repete o callback inteiro.
        Commit.ArmarCommitAmbiguo();
        (await ComServicoAsync(c.NegocioId, s => s.CancelarAvisandoClienteAsync(id))).Should().BeTrue();

        Commit.Falhas.Should().Be(1, "a falha simulada precisa ter acontecido, senão o teste não prova nada");
        var itens = await ItensDoAgendamentoAsync(id);
        itens.Count(i => i.Tipo == TipoNotificacao.Cancelamento).Should().Be(2, "uma por canal, não quatro");
        itens.Count(i => i.Tipo == TipoNotificacao.ProfissionalCancelado).Should().Be(2);

        await DespacharAsync();
        Email.Enviados.Count(e => e.Assunto.StartsWith("Agendamento cancelado")).Should().Be(2, "cliente e profissional, uma vez cada");
    }

    [Fact]
    public async Task Mensagem_de_agendamento_alterado_por_fora_e_cancelada_no_despacho_sem_envio()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        // Alteração que não passou pelo serviço (nenhuma invalidação explícita): o despachante confere o estado atual.
        await SqlAsync($"UPDATE agendamentos SET status = 'Cancelado' WHERE id = {id}");

        await DespacharAsync();

        Email.Chamadas.Should().Be(0);
        WhatsApp.Chamadas.Should().Be(0);
        (await ItensDoAgendamentoAsync(id)).Should().OnlyContain(i => i.Status == StatusNotificacao.Cancelada && i.UltimoErroCodigo == CodigosOutbox.AgendamentoObsoleto);
    }

    [Fact]
    public async Task Cliente_sem_email_recebe_so_por_whatsapp_e_sem_nenhum_canal_nao_gera_mensagem()
    {
        var c = await ArranjarAsync(whatsApp: true, clienteComEmail: false);
        var reservaId = (await ComServicoAsync(c.NegocioId, s => s.CriarReservaPublicaAsync(new CriarReservaPublica(c.ProfissionalId, [c.ServicoId], Hora(c, 10))))).AgendamentoId!.Value;
        (await ComServicoAsync(c.NegocioId, s => s.ConfirmarReservaPublicaAsync(new ConfirmarReservaPublica(
            reservaId, "Sem Email", "+5571966665555", null, null, null, "127.0.0.1")))).Sucesso.Should().BeTrue();

        var confirmacoes = (await ItensDoAgendamentoAsync(reservaId)).Where(i => i.Tipo == TipoNotificacao.Confirmacao).ToList();
        confirmacoes.Should().ContainSingle().Which.Canal.Should().Be(CanalNotificacao.WhatsApp);
    }

    [Fact]
    public async Task Confirmacao_publica_repetida_pelo_mesmo_telefone_nao_cria_outras_mensagens()
    {
        var c = await ArranjarAsync();
        var reservaId = (await ComServicoAsync(c.NegocioId, s => s.CriarReservaPublicaAsync(new CriarReservaPublica(c.ProfissionalId, [c.ServicoId], Hora(c, 10))))).AgendamentoId!.Value;
        var confirmar = new ConfirmarReservaPublica(reservaId, "Cliente Novo", "+5571977776666", "novo.cliente@teste.com", null, null, "127.0.0.1");

        (await ComServicoAsync(c.NegocioId, s => s.ConfirmarReservaPublicaAsync(confirmar))).Sucesso.Should().BeTrue();
        var antes = (await ItensDoAgendamentoAsync(reservaId)).Count;
        (await ComServicoAsync(c.NegocioId, s => s.ConfirmarReservaPublicaAsync(confirmar))).Sucesso.Should().BeTrue("duplo clique devolve o mesmo sucesso");

        (await ItensDoAgendamentoAsync(reservaId)).Should().HaveCount(antes).And.HaveCount(4);
    }

    [Fact]
    public async Task Cancelamento_pelo_link_do_cliente_avisa_so_o_profissional()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        (await ComServicoAsync(c.NegocioId, s => s.CancelarPelaGestaoPublicaAsync(id))).Should().Be(ResultadoCancelamentoPublico.Cancelado);

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Should().NotContain(i => i.Tipo == TipoNotificacao.Cancelamento, "foi o próprio cliente que cancelou");
        itens.Count(i => i.Tipo == TipoNotificacao.ProfissionalCancelado).Should().Be(2);
    }

    // ------------------------------------------------------------------ apoio

    private async Task<(Guid Id, string Email)> CriarOutroProfissionalAsync(CenarioOutbox c)
    {
        var email = $"outro-{Guid.NewGuid():N}@teste.com";
        var id = await Fabrica.NoBancoAsync(async db =>
        {
            var profissional = Profissional.Criar(c.NegocioId, "Outro Profissional", "+5571955554444", email);
            db.Profissionais.Add(profissional);
            foreach (var dia in new[] { Plataforma.Dominio.Comum.DiaSemana.Segunda })
            {
                db.HorariosTrabalho.Add(HorarioTrabalho.Criar(c.NegocioId, profissional.Id, dia, new TimeOnly(9, 0), new TimeOnly(12, 0)));
                db.HorariosTrabalho.Add(HorarioTrabalho.Criar(c.NegocioId, profissional.Id, dia, new TimeOnly(13, 0), new TimeOnly(18, 0)));
            }

            db.ProfissionalServicos.Add(Plataforma.Dominio.Servicos.ProfissionalServico.Criar(c.NegocioId, profissional.Id, c.ServicoId));
            await db.SaveChangesAsync();
            return profissional.Id;
        });
        return (id, email);
    }
}
