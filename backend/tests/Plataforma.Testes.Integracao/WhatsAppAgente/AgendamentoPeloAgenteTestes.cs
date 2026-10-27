using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.WhatsAppAgente;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Fatia 2 da Fase 3: reserva e confirmação do agendamento pelo agente (<c>ServicoAgendamentos</c>), sem ferramenta nem prompt —
/// os testes chamam o serviço direto, como a fatia 3 fará. Concorrência, idempotência, 9º dígito, tenant, assinatura e validade.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AgendamentoPeloAgenteTestes : IAsyncLifetime
{
    private const string Telefone = "+5571988887777";
    private const string TelefoneSem9 = "+557188887777";

    private readonly PostgresContainerFixture _postgres;
    private readonly InterceptadorDeCommit _commit = new();
    private readonly GanchoAntesDaTravaDaPendencia _gancho = new();
    private readonly GanchoAntesDaTravaDoCliente _ganchoCliente = new();
    private AmbienteAgente _ambiente = null!;

    public AgendamentoPeloAgenteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    private PlataformaWebApplicationFactory Fabrica => _ambiente.Fabrica;

    public async Task InitializeAsync()
    {
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(ajustarServicos: servicos =>
            servicos.ConfigureDbContext<PlataformaDbContext>(o => o.AddInterceptors(_commit, _gancho, _ganchoCliente)));
    }

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    // ---------------------------------------------------------------- reserva

    [Fact]
    public async Task Reserva_do_agente_nasce_reservada_sem_cliente_e_com_origem_whatsapp()
    {
        var c = await _ambiente.SemearAsync();

        var resultado = await ReservarAsync(c, c.Beto, c.Corte, H(10, 0));

        resultado.Sucesso.Should().BeTrue(resultado.MensagemErro);
        var a = await AgendamentoAsync(resultado.AgendamentoId!.Value);
        a.Status.Should().Be(StatusAgendamento.Reservado);
        a.Origem.Should().Be(OrigemAgendamento.WhatsApp);
        a.ClienteId.Should().BeNull();
        a.Forcado.Should().BeFalse();
        a.Inicio.Should().Be(H(10, 0));
        a.Fim.Should().Be(H(10, 30));
        a.ReservadoAte.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromMinutes(1));
        a.Servicos.Should().ContainSingle(s => s.ServicoId == c.Corte);
    }

    [Fact]
    public async Task Reserva_usa_preco_e_duracao_do_profissional()
    {
        var c = await _ambiente.SemearAsync();

        var resultado = await ReservarAsync(c, c.Ana, c.Corte, H(10, 0));

        resultado.Sucesso.Should().BeTrue(resultado.MensagemErro);
        var a = await AgendamentoAsync(resultado.AgendamentoId!.Value);
        a.Fim.Should().Be(H(11, 0), "o corte da Ana dura 60 min");
        a.Servicos.Single().Preco.Should().Be(70m);
    }

    [Theory]
    [InlineData(10, 7)]  // fora da grade de 15 min
    [InlineData(13, 0)]  // Beto só trabalha de manhã
    [InlineData(8, 0)]   // antes do expediente
    [InlineData(11, 45)] // termina depois do fim do turno
    public async Task Horario_fora_da_oferta_publica_e_recusado_com_alternativas(int hora, int minuto)
    {
        var c = await _ambiente.SemearAsync();

        var resultado = await ReservarAsync(c, c.Beto, c.Corte, H(hora, minuto));

        resultado.Sucesso.Should().BeFalse();
        resultado.Conflito.Should().BeTrue();
        resultado.ProximosHorariosLivres.Should().NotBeNull();
        resultado.ProximosHorariosLivres!.Count.Should().BeInRange(1, 5);
        (await ReservasAtivasAsync(c.Beto)).Should().Be(0);
    }

    [Fact]
    public async Task Horario_no_passado_e_recusado()
    {
        var c = await _ambiente.SemearAsync();
        var passado = SemeadorDeComissoes.Local(Dia().AddDays(-14), 10, 0);

        var resultado = await ReservarAsync(c, c.Beto, c.Corte, passado);

        resultado.Sucesso.Should().BeFalse();
        (await ReservasAtivasAsync(c.Beto)).Should().Be(0);
    }

    [Fact]
    public async Task Horario_ocupado_bloqueado_ou_forcado_e_recusado()
    {
        var c = await _ambiente.SemearAsync();
        await Fabrica.SemearAgendamentoAsync(c.NegocioId, c.Beto, H(9, 0), servicoId: c.Corte); // 9:00–9:30
        await Fabrica.NoBancoAsync(async db =>
        {
            db.BloqueiosAgenda.Add(BloqueioAgenda.Criar(c.NegocioId, c.Beto, H(10, 0), H(10, 30), "folga"));
            var forcado = Agendamento.CriarConfirmado(
                c.NegocioId, c.Beto, await ClienteQualquerAsync(db, c.NegocioId), H(11, 0),
                [new ItemServicoAgendamento(c.Corte, "Corte", 50m, 30)]);
            forcado.MarcarForcado("encaixe do dono", ["fora do expediente"], null, DateTimeOffset.UtcNow);
            db.Agendamentos.Add(forcado);
            return await db.SaveChangesAsync();
        });

        foreach (var hora in new[] { (9, 0), (10, 0), (11, 0) })
        {
            var resultado = await ReservarAsync(c, c.Beto, c.Corte, H(hora.Item1, hora.Item2));
            resultado.Sucesso.Should().BeFalse($"{hora.Item1}:{hora.Item2:00} não está livre");
            resultado.Conflito.Should().BeTrue();
            resultado.ProximosHorariosLivres!.Should().NotContain(H(hora.Item1, hora.Item2));
        }
    }

    [Fact]
    public async Task Profissional_inativo_servico_que_ele_nao_faz_e_ids_de_outro_negocio_sao_recusados()
    {
        var c = await _ambiente.SemearAsync();
        var outro = await _ambiente.SemearAsync("Outro negócio");

        (await ReservarAsync(c, c.Inativo, c.ServicoDoInativo, H(10, 0))).Sucesso.Should().BeFalse("profissional inativo");
        (await ReservarAsync(c, c.Beto, c.Tratamento, H(10, 0))).Sucesso.Should().BeFalse("Beto não faz tratamento");
        (await ReservarAsync(c, outro.Beto, c.Corte, H(10, 0))).Sucesso.Should().BeFalse("profissional de outro negócio");
        (await ReservarAsync(c, c.Beto, outro.Corte, H(10, 0))).Sucesso.Should().BeFalse("serviço de outro negócio");

        await Fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE servicos SET ativo = false WHERE id = {c.Barba}");
            return 0;
        });
        (await ReservarAsync(c, c.Beto, c.Barba, H(10, 0))).Sucesso.Should().BeFalse("serviço inativo");

        (await ReservasAtivasAsync(c.Beto)).Should().Be(0);
        (await ReservasAtivasAsync(outro.Beto)).Should().Be(0);
    }

    [Fact]
    public async Task Assinatura_suspensa_nao_reserva_nem_confirma()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        await SuspenderAssinaturaAsync(c.NegocioId);

        (await ReservarAsync(c, c.Beto, c.Corte, H(11, 0))).Sucesso.Should().BeFalse();

        var confirmacao = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria", Guid.NewGuid());
        confirmacao.Sucesso.Should().BeFalse();
        confirmacao.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.AssinaturaSuspensa);
        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Reservado);
        (await ClientesAsync(c.NegocioId)).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- concorrência

    [Theory]
    [InlineData(2)]
    [InlineData(20)]
    public async Task Pedidos_simultaneos_do_mesmo_horario_dao_uma_reserva_e_recusas_educadas(int quantidade)
    {
        var c = await _ambiente.SemearAsync();

        var resultados = await Task.WhenAll(Enumerable.Range(0, quantidade).Select(_ => Task.Run(() => ReservarAsync(c, c.Beto, c.Corte, H(10, 0)))));

        resultados.Count(r => r.Sucesso).Should().Be(1);
        var recusas = resultados.Where(r => !r.Sucesso).ToList();
        recusas.Should().HaveCount(quantidade - 1);
        recusas.Should().OnlyContain(r => r.Conflito && r.ProximosHorariosLivres != null && r.ProximosHorariosLivres.Count <= 5);
        (await ReservasAtivasAsync(c.Beto)).Should().Be(1);
    }

    [Fact]
    public async Task Agente_e_assistente_publico_disputando_o_mesmo_horario_dao_um_vencedor()
    {
        var c = await _ambiente.SemearAsync();

        var tarefas = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            using var escopo = Escopo(c.NegocioId);
            var servico = escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>();
            return i % 2 == 0
                ? await servico.CriarReservaAgenteAsync(new CriarReservaAgente(c.Beto, c.Corte, H(10, 0)))
                : await servico.CriarReservaPublicaAsync(new CriarReservaPublica(c.Beto, [c.Corte], H(10, 0)));
        }));
        var resultados = await Task.WhenAll(tarefas);

        resultados.Count(r => r.Sucesso).Should().Be(1);
        (await ReservasAtivasAsync(c.Beto)).Should().Be(1);
    }

    [Fact]
    public async Task Duas_conversas_de_negocios_diferentes_reservam_e_confirmam_em_paralelo_cada_uma_no_seu_negocio()
    {
        var a = await _ambiente.SemearAsync("Negócio A");
        var b = await _ambiente.SemearAsync("Negócio B");

        var (pa, pb) = await (Task.Run(() => PreparadaAsync(a, Telefone, "Ana Cliente", H(10, 0))), Task.Run(() => PreparadaAsync(b, Telefone, "Bia Cliente", H(10, 0)))).Pair();
        var (ra, rb) = await (Task.Run(() => ConfirmarAsync(a, pa.PendenciaId, Telefone, null, Guid.NewGuid())), Task.Run(() => ConfirmarAsync(b, pb.PendenciaId, Telefone, null, Guid.NewGuid()))).Pair();

        ra.Sucesso.Should().BeTrue();
        rb.Sucesso.Should().BeTrue();
        (await AgendamentoAsync(pa.AgendamentoId)).NegocioId.Should().Be(a.NegocioId);
        (await AgendamentoAsync(pb.AgendamentoId)).NegocioId.Should().Be(b.NegocioId);
        (await ClientesAsync(a.NegocioId)).Should().ContainSingle(x => x.Nome == "Ana Cliente");
        (await ClientesAsync(b.NegocioId)).Should().ContainSingle(x => x.Nome == "Bia Cliente");
    }

    // ---------------------------------------------------------------- confirmação

    [Fact]
    public async Task Confirmacao_de_cliente_novo_cria_o_cadastro_na_forma_de_13_digitos_e_registra_tudo()
    {
        var c = await _ambiente.SemearAsync();
        await DefinirEmailDoProfissionalAsync(c.Beto, "beto@teste.com");
        var pendente = await PreparadaAsync(c, Telefone, "Maria Nova", H(10, 0));
        var sim = Guid.NewGuid();

        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria Nova", sim);

        r.Sucesso.Should().BeTrue();
        r.ConfirmadaAgora.Should().BeTrue();
        r.AgendamentoId.Should().Be(pendente.AgendamentoId);

        var a = await AgendamentoAsync(pendente.AgendamentoId);
        a.Status.Should().Be(StatusAgendamento.Agendado);
        a.Origem.Should().Be(OrigemAgendamento.WhatsApp);
        a.ReservadoAte.Should().BeNull();
        a.ClienteId.Should().NotBeNull();
        a.ConsentimentoData.Should().NotBeNull();
        a.ConsentimentoVersaoTermos.Should().Be(VersaoTermos.Atual);
        a.ConsentimentoIp.Should().BeNull();
        a.NomeInformado.Should().BeNull();
        a.PodeReceberMensagens.Should().BeTrue();

        var clientes = await ClientesAsync(c.NegocioId);
        var cliente = clientes.Should().ContainSingle().Subject;
        cliente.Id.Should().Be(a.ClienteId!.Value);
        cliente.Nome.Should().Be("Maria Nova");
        cliente.Origem.Should().Be(OrigemCliente.WhatsApp);
        cliente.Telefone!.Valor.Should().Be(Telefone);

        var p = await PendenciaAsync(pendente.PendenciaId);
        p.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Confirmada);
        p.ConfirmacaoMensagemId.Should().Be(sim);
        p.VersaoTermos.Should().Be(VersaoTermos.Atual);
        p.NomeInformado.Should().BeNull("o nome digitado é apagado ao sair de Aguardando");
        p.ConcluidaEm.Should().NotBeNull();

        (await ConversaAsync(pendente.ConversaId)).ClienteId.Should().Be(cliente.Id);

        var logs = await Fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .Where(l => l.NegocioId == c.NegocioId && l.Acao == "AgendarPeloWhatsApp").ToListAsync());
        var log = logs.Should().ContainSingle().Subject;
        log.Entidade.Should().Be("Agendamento");
        log.EntidadeId.Should().Be(a.Id);
        log.Autor.Should().Be("sistema");
        log.Detalhes.Should().Contain(pendente.PendenciaId.ToString()).And.Contain("Corte").And.Contain("Beto");
        log.Detalhes.Should().NotContain("Maria").And.NotContain("5571988887777").And.NotContain("988887777");

        // Profissional avisado (e-mail); o cliente não tem e-mail, então nada mais sai.
        Email().Enviados.Should().ContainSingle(e => e.Destinatario == "beto@teste.com");
        Email().Enviados.Should().HaveCount(1);
    }

    [Fact]
    public async Task Confirmacao_nunca_envia_o_whatsapp_da_plataforma_ao_cliente_e_o_email_so_se_ele_tiver()
    {
        var c = await _ambiente.SemearAsync();
        await LigarWhatsAppDeConfirmacaoAsync(c.NegocioId);
        await Fabrica.NoBancoAsync(async db =>
        {
            db.Clientes.Add(Cliente.Criar(c.NegocioId, "Com Email", TelefoneE164.Criar(Telefone), OrigemCliente.Painel, "cliente@teste.com"));
            return await db.SaveChangesAsync();
        });
        var pendente = await PreparadaAsync(c, Telefone, null, H(10, 0));

        (await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, Guid.NewGuid())).Sucesso.Should().BeTrue();

        Fabrica.Services.GetRequiredService<EspiaWhatsApp>().Enviados.Should().NotContain(e => e.Telefone.Valor == Telefone);
        Email().Enviados.Should().ContainSingle(e => e.Destinatario == "cliente@teste.com");
    }

    [Fact]
    public async Task Cliente_existente_com_o_9o_digito_e_o_mesmo_cliente_nas_duas_formas_e_o_cadastro_nao_muda()
    {
        var c = await _ambiente.SemearAsync();
        var existente = await Fabrica.NoBancoAsync(async db =>
        {
            var cli = Cliente.Criar(c.NegocioId, "Maria Cadastro", TelefoneE164.Criar(Telefone), OrigemCliente.Painel, "maria@cadastro.com", "obs");
            db.Clientes.Add(cli);
            await db.SaveChangesAsync();
            return cli.Id;
        });
        // A conversa chega pelo número SEM o 9º dígito (como algumas contas do WhatsApp).
        var pendente = await PreparadaAsync(c, TelefoneSem9, "Outro Nome", H(10, 0));

        var r = await ConfirmarAsync(c, pendente.PendenciaId, TelefoneSem9, "Outro Nome", Guid.NewGuid());

        r.Sucesso.Should().BeTrue();
        var clientes = await ClientesAsync(c.NegocioId);
        clientes.Should().ContainSingle("não duplica o cliente");
        var cliente = clientes[0];
        cliente.Id.Should().Be(existente);
        cliente.Nome.Should().Be("Maria Cadastro");
        cliente.Email.Should().Be("maria@cadastro.com");
        cliente.Observacoes.Should().Be("obs");
        cliente.Telefone!.Valor.Should().Be(Telefone);
        cliente.Origem.Should().Be(OrigemCliente.Painel);
        var a = await AgendamentoAsync(pendente.AgendamentoId);
        a.ClienteId.Should().Be(existente);
        a.NomeInformado.Should().BeNull("o nome do modelo é ignorado para cliente existente");
    }

    [Fact]
    public async Task Cliente_cadastrado_sem_o_9o_digito_e_encontrado_pela_conversa_com_o_9()
    {
        var c = await _ambiente.SemearAsync();
        var existente = await Fabrica.NoBancoAsync(async db =>
        {
            var cli = Cliente.Criar(c.NegocioId, "Antiga", TelefoneE164.Criar(TelefoneSem9));
            db.Clientes.Add(cli);
            await db.SaveChangesAsync();
            return cli.Id;
        });
        var pendente = await PreparadaAsync(c, Telefone, null, H(10, 0));

        (await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, Guid.NewGuid())).Sucesso.Should().BeTrue();

        (await ClientesAsync(c.NegocioId)).Should().ContainSingle().Which.Id.Should().Be(existente);
    }

    [Fact]
    public async Task Cliente_novo_sem_nome_nao_grava_nada()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, null, H(10, 0));

        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, Guid.NewGuid());

        r.Sucesso.Should().BeFalse();
        r.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.NomeNecessario);
        (await ClientesAsync(c.NegocioId)).Should().BeEmpty();
        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Reservado);
        (await PendenciaAsync(pendente.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);

        // O cliente informa o nome e a mesma pendência ainda confirma.
        (await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Joana", Guid.NewGuid())).Sucesso.Should().BeTrue();
    }

    [Fact]
    public async Task Mesma_mensagem_de_confirmacao_repetida_nao_duplica_nada()
    {
        var c = await _ambiente.SemearAsync();
        await DefinirEmailDoProfissionalAsync(c.Beto, "beto@teste.com");
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        var sim = Guid.NewGuid();

        var primeira = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, sim);
        var repetida = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, sim);
        var outroSim = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, Guid.NewGuid());

        primeira.ConfirmadaAgora.Should().BeTrue();
        repetida.Sucesso.Should().BeTrue();
        repetida.ConfirmadaAgora.Should().BeFalse();
        repetida.AgendamentoId.Should().Be(pendente.AgendamentoId);
        outroSim.Sucesso.Should().BeTrue();
        outroSim.ConfirmadaAgora.Should().BeFalse();

        (await ClientesAsync(c.NegocioId)).Should().HaveCount(1);
        (await Fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().CountAsync(a => a.NegocioId == c.NegocioId))).Should().Be(1);
        (await PendenciaAsync(pendente.PendenciaId)).ConfirmacaoMensagemId.Should().Be(sim);
        Email().Enviados.Count(e => e.Destinatario == "beto@teste.com").Should().Be(1, "um único aviso ao profissional");
        (await Fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.Acao == "AgendarPeloWhatsApp"))).Should().Be(1);
    }

    [Fact]
    public async Task Vinte_confirmacoes_simultaneas_confirmam_uma_vez()
    {
        var c = await _ambiente.SemearAsync();
        await DefinirEmailDoProfissionalAsync(c.Beto, "beto@teste.com");
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, Guid.NewGuid()))));

        resultados.Should().OnlyContain(r => r.Sucesso);
        resultados.Count(r => r.ConfirmadaAgora).Should().Be(1);
        (await ClientesAsync(c.NegocioId)).Should().HaveCount(1);
        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Agendado);
        Email().Enviados.Count(e => e.Destinatario == "beto@teste.com").Should().Be(1);
        (await Fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.Acao == "AgendarPeloWhatsApp"))).Should().Be(1);
    }

    [Fact]
    public async Task Confirmacoes_simultaneas_de_conversas_do_mesmo_telefone_em_horarios_diferentes_criam_um_cliente_so()
    {
        var c = await _ambiente.SemearAsync();
        var uma = await PreparadaAsync(c, Telefone, "Maria", H(10, 0), c.Beto);
        var duas = await PreparadaAsync(c, TelefoneSem9, "Maria", H(11, 0), c.Beto);

        var resultados = await Task.WhenAll(
            Task.Run(() => ConfirmarAsync(c, uma.PendenciaId, Telefone, null, Guid.NewGuid())),
            Task.Run(() => ConfirmarAsync(c, duas.PendenciaId, TelefoneSem9, null, Guid.NewGuid())));

        resultados.Should().OnlyContain(r => r.Sucesso);
        (await ClientesAsync(c.NegocioId)).Should().HaveCount(1, "as duas formas do 9º dígito são o mesmo cliente");
    }

    // ---------------------------------------------------------------- isolamento e validade

    [Fact]
    public async Task Pendencia_de_outro_negocio_ou_de_outra_conversa_nao_confirma()
    {
        var a = await _ambiente.SemearAsync("Negócio A");
        var b = await _ambiente.SemearAsync("Negócio B");
        var pa = await PreparadaAsync(a, Telefone, "Maria", H(10, 0));

        // Mesmo telefone, outro negócio.
        var deB = await ConfirmarAsync(b, pa.PendenciaId, Telefone, "Maria", Guid.NewGuid());
        // Mesmo negócio, telefone de outra conversa.
        var deOutroNumero = await ConfirmarAsync(a, pa.PendenciaId, "+5571977776666", "Maria", Guid.NewGuid());

        deB.Sucesso.Should().BeFalse();
        deB.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.NaoEncontrada);
        deOutroNumero.Sucesso.Should().BeFalse();
        deOutroNumero.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.NaoEncontrada);
        (await AgendamentoAsync(pa.AgendamentoId)).Status.Should().Be(StatusAgendamento.Reservado);
        (await ClientesAsync(a.NegocioId)).Should().BeEmpty();
        (await ClientesAsync(b.NegocioId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Pendencia_vencida_nao_confirma_e_libera_o_horario()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        await Fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pendencias_agendamento_whatsapp SET expira_em = now() - interval '1 minute' WHERE id = {pendente.PendenciaId}");
            return 0;
        });

        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria", Guid.NewGuid());

        r.Sucesso.Should().BeFalse();
        r.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.Expirada);
        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Expirado);
        (await PendenciaAsync(pendente.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Expirada);
        (await ClientesAsync(c.NegocioId)).Should().BeEmpty();
        (await ReservarAsync(c, c.Beto, c.Corte, H(10, 0))).Sucesso.Should().BeTrue("o horário voltou a ficar livre");
    }

    [Fact]
    public async Task Job_expira_pendencia_abandonada_limpa_o_nome_e_libera_o_horario_sem_nova_mensagem()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        await VencerAsync(pendente);

        await ExecutarJobAsync();

        var p = await PendenciaAsync(pendente.PendenciaId);
        p.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Expirada);
        p.ConcluidaEm.Should().NotBeNull();
        p.NomeInformado.Should().BeNull();
        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Expirado);
        (await ReservarAsync(c, c.Beto, c.Corte, H(10, 0))).Sucesso.Should().BeTrue("o horário voltou a ficar livre");
    }

    [Fact]
    public async Task Job_nao_toca_pendencia_confirmada_nem_ainda_valida_e_isola_os_negocios()
    {
        var a = await _ambiente.SemearAsync("Negócio A");
        var b = await _ambiente.SemearAsync("Negócio B");
        var vencidaA = await PreparadaAsync(a, Telefone, "Maria", H(10, 0));
        var validaA = await PreparadaAsync(a, "+5571977776666", "Joana", H(11, 0));
        var confirmadaB = await PreparadaAsync(b, Telefone, "Maria", H(10, 0));
        var vencidaB = await PreparadaAsync(b, "+5571966665555", "Ana", H(11, 0));
        (await ConfirmarAsync(b, confirmadaB.PendenciaId, Telefone, "Maria", Guid.NewGuid())).Sucesso.Should().BeTrue();
        await VencerAsync(vencidaA);
        await VencerAsync(vencidaB);
        await VencerAsync(confirmadaB); // confirmada com prazo vencido: continua intacta

        await ExecutarJobAsync();

        (await PendenciaAsync(vencidaA.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Expirada);
        (await PendenciaAsync(vencidaB.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Expirada);
        var valida = await PendenciaAsync(validaA.PendenciaId);
        valida.Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
        valida.NomeInformado.Should().Be("Joana");
        (await AgendamentoAsync(validaA.AgendamentoId)).Status.Should().Be(StatusAgendamento.Reservado);
        (await PendenciaAsync(confirmadaB.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Confirmada);
        (await AgendamentoAsync(confirmadaB.AgendamentoId)).Status.Should().Be(StatusAgendamento.Agendado);
    }

    private Task VencerAsync(Preparada p) => Fabrica.NoBancoAsync(async db =>
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE pendencias_agendamento_whatsapp SET expira_em = now() - interval '1 minute' WHERE id = {p.PendenciaId}");
        return 0;
    });

    private async Task ExecutarJobAsync()
    {
        using var escopo = Fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<Plataforma.Infraestrutura.Agendamentos.JobExpirarReservas>().ExecutarAsync();
    }

    [Fact]
    public async Task Reserva_expirada_pelo_prazo_nao_confirma()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        await Fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE agendamentos SET reservado_ate = now() - interval '1 minute' WHERE id = {pendente.AgendamentoId}");
            return 0;
        });

        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria", Guid.NewGuid());

        r.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.Expirada);
        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Expirado);
        (await PendenciaAsync(pendente.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Expirada);
    }

    [Theory]
    [InlineData(StatusPendenciaAgendamentoWhatsApp.Recusada)]
    [InlineData(StatusPendenciaAgendamentoWhatsApp.Substituida)]
    [InlineData(StatusPendenciaAgendamentoWhatsApp.Expirada)]
    [InlineData(StatusPendenciaAgendamentoWhatsApp.Falhou)]
    public async Task Pendencia_ja_encerrada_nao_confirma(StatusPendenciaAgendamentoWhatsApp status)
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        var texto = status.ToString();
        await Fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE pendencias_agendamento_whatsapp SET status = {texto} WHERE id = {pendente.PendenciaId}");
            return 0;
        });

        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria", Guid.NewGuid());

        r.Sucesso.Should().BeFalse();
        r.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.Encerrada);
        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Reservado);
    }

    [Fact]
    public async Task Profissional_desativado_entre_o_preparo_e_o_sim_nao_agenda_e_libera_a_reserva()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        await Fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET ativo = false WHERE id = {c.Beto}");
            return 0;
        });

        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria", Guid.NewGuid());

        r.Sucesso.Should().BeFalse();
        r.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.IndisponivelAgora);
        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Expirado);
        (await PendenciaAsync(pendente.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Falhou);
        (await ClientesAsync(c.NegocioId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Bloqueio_criado_depois_da_reserva_impede_a_confirmacao()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        await Fabrica.NoBancoAsync(async db =>
        {
            db.BloqueiosAgenda.Add(BloqueioAgenda.Criar(c.NegocioId, c.Beto, H(9, 30), H(10, 15), "médico"));
            return await db.SaveChangesAsync();
        });

        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria", Guid.NewGuid());

        r.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.IndisponivelAgora);
        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Expirado);
    }

    [Fact]
    public async Task Servico_excluido_ou_desvinculado_entre_o_preparo_e_o_sim_impede_a_confirmacao()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        await Fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM profissional_servicos WHERE profissional_id = {c.Beto} AND servico_id = {c.Corte}");
            return 0;
        });

        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria", Guid.NewGuid());

        r.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.IndisponivelAgora);
    }

    // ---------------------------------------------------------------- LGPD e web

    [Fact]
    public async Task Anonimizar_o_cliente_limpa_o_vinculo_da_conversa()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria", Guid.NewGuid());
        r.Sucesso.Should().BeTrue();
        var clienteId = (await ClientesAsync(c.NegocioId)).Single().Id;
        (await ConversaAsync(pendente.ConversaId)).ClienteId.Should().Be(clienteId);

        using (var escopo = Escopo(c.NegocioId))
            (await escopo.ServiceProvider.GetRequiredService<IGerenciadorClientes>().ExcluirAsync(clienteId)).Should().BeTrue();

        (await ConversaAsync(pendente.ConversaId)).ClienteId.Should().BeNull();
        (await ClientesAsync(c.NegocioId)).Single().Excluido.Should().BeTrue();
    }

    [Fact]
    public async Task Excluir_cliente_de_outro_negocio_nao_espera_a_trava_dele_e_devolve_nao_encontrado()
    {
        var a = await _ambiente.SemearAsync();
        var b = await _ambiente.SemearAsync();
        var clienteB = await Fabrica.NoBancoAsync(async db =>
        {
            var cli = Cliente.Criar(b.NegocioId, "Cliente do B", TelefoneE164.Criar(Telefone));
            db.Clientes.Add(cli);
            await db.SaveChangesAsync();
            return cli.Id;
        });

        // Uma transação do negócio B segura a linha do cliente de B.
        using var escopoTrava = Fabrica.Services.CreateScope();
        var dbTrava = escopoTrava.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        await using var trava = await dbTrava.Database.BeginTransactionAsync();
        await dbTrava.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM clientes WHERE id = {clienteB} FOR UPDATE");

        // O negócio A tenta excluir o mesmo id: não é dele, então não pode nem esperar a trava de B.
        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        using var escopo = Escopo(a.NegocioId);
        var tarefa = escopo.ServiceProvider.GetRequiredService<IGerenciadorClientes>().ExcluirAsync(clienteB);
        var terminou = await Task.WhenAny(tarefa, Task.Delay(TimeSpan.FromSeconds(3))) == tarefa;

        terminou.Should().BeTrue("a exclusão de A não pode esperar a trava do cliente de B");
        cronometro.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        (await tarefa).Should().BeFalse("o cliente não pertence ao negócio de A");

        await trava.RollbackAsync();
        (await ClientesAsync(b.NegocioId)).Single().Excluido.Should().BeFalse();
    }

    [Fact]
    public async Task Anonimizacao_que_passa_entre_a_identificacao_e_o_vinculo_nao_deixa_o_agendamento_no_cadastro_anonimizado()
    {
        var c = await _ambiente.SemearAsync();
        var antigo = await Fabrica.NoBancoAsync(async db =>
        {
            var cli = Cliente.Criar(c.NegocioId, "Maria Antiga", TelefoneE164.Criar(Telefone));
            db.Clientes.Add(cli);
            await db.SaveChangesAsync();
            return cli.Id;
        });
        var pendente = await PreparadaAsync(c, Telefone, "Maria Nova", H(10, 0));

        // A confirmação já identificou o cliente antigo; antes de travá-lo, a anonimização roda por inteiro.
        _ganchoCliente.Armar(async () =>
        {
            using var escopo = Escopo(c.NegocioId);
            (await escopo.ServiceProvider.GetRequiredService<IGerenciadorClientes>().ExcluirAsync(antigo)).Should().BeTrue();
        });

        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, Guid.NewGuid());

        _ganchoCliente.Disparou.Should().BeTrue("o gancho precisa ter rodado no meio da confirmação");
        r.Sucesso.Should().BeTrue();
        var agendamento = await AgendamentoAsync(pendente.AgendamentoId);
        agendamento.ClienteId.Should().NotBeNull().And.NotBe(antigo, "o cadastro anonimizado nunca recebe o agendamento");
        var clientes = await ClientesAsync(c.NegocioId);
        clientes.Single(x => x.Id == antigo).Excluido.Should().BeTrue();
        var novo = clientes.Single(x => x.Id == agendamento.ClienteId);
        novo.Excluido.Should().BeFalse();
        novo.Nome.Should().Be("Maria Nova");
        novo.Telefone!.Valor.Should().Be(Telefone);
        (await ConversaAsync(pendente.ConversaId)).ClienteId.Should().Be(novo.Id, "a conversa não volta ao id anonimizado");
    }

    [Fact]
    public async Task Nome_invalido_na_confirmacao_e_recusado_sem_gravar_nada()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));

        foreach (var invalido in new[] { "<b>Maria</b>", "Maria 2", "www.golpe.com", "M", new string('a', 81) })
        {
            var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, invalido, Guid.NewGuid());
            r.Sucesso.Should().BeFalse(invalido);
            r.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.NomeInvalido, invalido);
        }

        (await ClientesAsync(c.NegocioId)).Should().BeEmpty();
        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Reservado);
        (await PendenciaAsync(pendente.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);

        // Com um nome bom a mesma pendência confirma.
        (await ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria", Guid.NewGuid())).Sucesso.Should().BeTrue();
    }

    [Fact]
    public async Task Nome_invalido_guardado_na_pendencia_tambem_e_recusado_para_cliente_novo()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria 2", H(10, 0));

        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, Guid.NewGuid());

        r.Sucesso.Should().BeFalse();
        r.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.NomeInvalido);
        (await ClientesAsync(c.NegocioId)).Should().BeEmpty();
        (await PendenciaAsync(pendente.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Aguardando);
    }

    [Fact]
    public async Task Reserva_e_confirmacao_publicas_continuam_com_origem_link_publico()
    {
        var c = await _ambiente.SemearAsync();
        using var escopo = Escopo(c.NegocioId);
        var servico = escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>();

        var reserva = await servico.CriarReservaPublicaAsync(new CriarReservaPublica(c.Beto, [c.Corte], H(10, 0)));
        reserva.Sucesso.Should().BeTrue();
        var confirmada = await servico.ConfirmarReservaPublicaAsync(
            new ConfirmarReservaPublica(reserva.AgendamentoId!.Value, "Pública", "71988880000", null, null, null, "127.0.0.1"));

        confirmada.Sucesso.Should().BeTrue();
        var a = await AgendamentoAsync(reserva.AgendamentoId.Value);
        a.Origem.Should().Be(OrigemAgendamento.LinkPublico);
        a.ConsentimentoIp.Should().Be("127.0.0.1");
        (await ClientesAsync(c.NegocioId)).Single().Origem.Should().Be(OrigemCliente.LinkPublico);
    }

    // ---------------------------------------------------------------- apoio

    // ------------------------------------------- quem confirmou agora (retentativas) e instante da checagem

    [Fact]
    public async Task Falha_transitoria_antes_do_commit_repete_e_a_propria_requisicao_confirma_uma_vez()
    {
        var c = await _ambiente.SemearAsync();
        await DefinirEmailDoProfissionalAsync(c.Beto, "beto@teste.com");
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));

        _commit.ArmarFalhaAntesDoCommit();
        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, null);

        _commit.Falhas.Should().Be(1, "a falha simulada precisa ter acontecido");
        r.Sucesso.Should().BeTrue();
        r.ConfirmadaAgora.Should().BeTrue("a repetição foi quem gravou");
        (await PendenciaAsync(pendente.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Confirmada);
        Email().Enviados.Count(e => e.Destinatario == "beto@teste.com").Should().Be(1);
        (await Fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.Acao == "AgendarPeloWhatsApp"))).Should().Be(1);
    }

    [Fact]
    public async Task Commit_ambiguo_confirmado_pelo_servidor_e_reconhecido_na_repeticao_sem_duplicar()
    {
        var c = await _ambiente.SemearAsync();
        await DefinirEmailDoProfissionalAsync(c.Beto, "beto@teste.com");
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));

        _commit.ArmarCommitAmbiguo();
        var r = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, Guid.NewGuid());

        _commit.Falhas.Should().Be(1);
        r.Sucesso.Should().BeTrue();
        r.ConfirmadaAgora.Should().BeTrue("a resposta do commit se perdeu, mas foi esta requisição que gravou");
        (await ClientesAsync(c.NegocioId)).Should().HaveCount(1);
        Email().Enviados.Count(e => e.Destinatario == "beto@teste.com").Should().Be(1, "um único aviso ao profissional");
        (await Fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.Acao == "AgendarPeloWhatsApp"))).Should().Be(1);
    }

    [Fact]
    public async Task Requisicao_que_falhou_antes_do_commit_nao_se_acha_vencedora_quando_outra_confirma_no_meio_mesmo_com_ids_nulos()
    {
        var c = await _ambiente.SemearAsync();
        await DefinirEmailDoProfissionalAsync(c.Beto, "beto@teste.com");
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));

        ResultadoConfirmacaoAgente? vencedora = null;
        // A 1ª tentativa da requisição A falha antes do commit; antes de a 2ª travar a pendência, a requisição B confirma.
        _commit.ArmarFalhaAntesDoCommit();
        _gancho.Armar(1, async () => vencedora = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, null));

        var a = await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, null);

        vencedora.Should().NotBeNull();
        vencedora!.Sucesso.Should().BeTrue();
        vencedora.ConfirmadaAgora.Should().BeTrue();
        a.Sucesso.Should().BeTrue();
        a.ConfirmadaAgora.Should().BeFalse("quem gravou foi a outra requisição");
        a.AgendamentoId.Should().Be(vencedora.AgendamentoId);
        Email().Enviados.Count(e => e.Destinatario == "beto@teste.com").Should().Be(1, "um único aviso ao profissional");
        (await Fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.Acao == "AgendarPeloWhatsApp"))).Should().Be(1);
    }

    [Fact]
    public async Task Espera_pela_trava_que_atravessa_o_prazo_da_pendencia_nao_confirma()
    {
        var c = await _ambiente.SemearAsync();
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        await Fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pendencias_agendamento_whatsapp SET expira_em = now() + interval '3 seconds' WHERE id = {pendente.PendenciaId}");
            return 0;
        });

        // Outra transação segura a linha do agendamento até passar do prazo.
        using var escopoTrava = Fabrica.Services.CreateScope();
        var dbTrava = escopoTrava.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        await using (var trava = await dbTrava.Database.BeginTransactionAsync())
        {
            await dbTrava.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM agendamentos WHERE id = {pendente.AgendamentoId} FOR UPDATE");

            var tarefa = ConfirmarAsync(c, pendente.PendenciaId, Telefone, "Maria", Guid.NewGuid());
            await Task.Delay(TimeSpan.FromSeconds(4.5));
            tarefa.IsCompleted.Should().BeFalse("está esperando a trava da linha");
            await trava.CommitAsync();

            var r = await tarefa;
            r.Sucesso.Should().BeFalse();
            r.Motivo.Should().Be(MotivoRecusaConfirmacaoAgente.Expirada);
        }

        (await AgendamentoAsync(pendente.AgendamentoId)).Status.Should().Be(StatusAgendamento.Expirado);
        (await PendenciaAsync(pendente.PendenciaId)).Status.Should().Be(StatusPendenciaAgendamentoWhatsApp.Expirada);
        (await ClientesAsync(c.NegocioId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelamento_avisando_o_cliente_do_agente_sem_email_sai_pelo_whatsapp_sem_outbox()
    {
        var c = await _ambiente.SemearAsync();
        await LigarWhatsAppDeConfirmacaoAsync(c.NegocioId);
        var pendente = await PreparadaAsync(c, Telefone, "Maria", H(10, 0));
        (await ConfirmarAsync(c, pendente.PendenciaId, Telefone, null, Guid.NewGuid())).Sucesso.Should().BeTrue();
        var espia = Fabrica.Services.GetRequiredService<EspiaWhatsApp>();
        espia.Enviados.Clear();

        using (var escopo = Escopo(c.NegocioId))
            (await escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>().CancelarAvisandoClienteAsync(pendente.AgendamentoId)).Should().BeTrue();

        espia.Enviados.Should().ContainSingle(e => e.Telefone.Valor == Telefone, "o aviso de cancelamento mantém o telefone; só a confirmação inicial o dispensa");
    }

    /// <summary>Roda uma ação antes de a confirmação travar a pendência (depois de pular as N primeiras vezes), uma única vez.</summary>
    private sealed class GanchoAntesDaTravaDaPendencia : DbCommandInterceptor
    {
        private Func<Task>? _acao;
        private int _pular;

        public void Armar(int pular, Func<Task> acao) => (_pular, _acao) = (pular, acao);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_acao is not null && command.CommandText.Contains("FROM pendencias_agendamento_whatsapp") && command.CommandText.Contains("FOR UPDATE"))
            {
                if (_pular > 0)
                    _pular--;
                else
                {
                    var acao = _acao;
                    _acao = null;
                    await acao();
                }
            }

            return result;
        }
    }

    /// <summary>Roda uma ação (uma só vez) antes de a confirmação travar a linha do cliente, isto é, depois de identificá-lo.</summary>
    private sealed class GanchoAntesDaTravaDoCliente : DbCommandInterceptor
    {
        private Func<Task>? _acao;

        public bool Disparou { get; private set; }

        public void Armar(Func<Task> acao) => (_acao, Disparou) = (acao, false);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_acao is not null && command.CommandText.Contains("FROM clientes") && command.CommandText.Contains("FOR UPDATE"))
            {
                var acao = _acao;
                _acao = null;
                Disparou = true;
                await acao();
            }

            return result;
        }
    }

    private sealed record Preparada(Guid PendenciaId, Guid AgendamentoId, Guid ConversaId);

    private static DateOnly Dia() => AmbienteAgente.ProximaSegunda();

    private static DateTimeOffset H(int hora, int minuto) => SemeadorDeComissoes.Local(Dia(), hora, minuto);

    private EspiaEmail Email() => Fabrica.Services.GetRequiredService<EspiaEmail>();

    private IServiceScope Escopo(Guid negocioId)
    {
        var escopo = Fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioId);
        return escopo;
    }

    private async Task<ResultadoAgendamento> ReservarAsync(CenarioAgente c, Guid profissionalId, Guid servicoId, DateTimeOffset inicio)
    {
        using var escopo = Escopo(c.NegocioId);
        return await escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>()
            .CriarReservaAgenteAsync(new CriarReservaAgente(profissionalId, servicoId, inicio));
    }

    private async Task<ResultadoConfirmacaoAgente> ConfirmarAsync(CenarioAgente c, Guid pendenciaId, string telefone, string? nome, Guid? mensagemId)
    {
        using var escopo = Escopo(c.NegocioId);
        return await escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>()
            .ConfirmarReservaAgenteAsync(new ConfirmarReservaAgente(pendenciaId, telefone, nome, mensagemId));
    }

    /// <summary>Reserva pelo serviço + conversa + pendência gravadas direto no banco, como a ferramenta da fatia 3 fará.</summary>
    private async Task<Preparada> PreparadaAsync(
        CenarioAgente c, string telefone, string? nome, DateTimeOffset inicio, Guid? profissionalId = null)
    {
        var reserva = await ReservarAsync(c, profissionalId ?? c.Beto, c.Corte, inicio);
        reserva.Sucesso.Should().BeTrue(reserva.MensagemErro);

        return await Fabrica.NoBancoAsync(async db =>
        {
            var instancia = await db.InstanciasWhatsApp.IgnoreQueryFilters().FirstAsync(i => i.NegocioId == c.NegocioId);
            var conversa = await db.ConversasWhatsApp.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.NegocioId == c.NegocioId && x.TelefoneCliente == telefone);
            if (conversa is null)
            {
                conversa = new ConversaWhatsApp(c.NegocioId, instancia.Id, telefone, DateTimeOffset.UtcNow);
                db.ConversasWhatsApp.Add(conversa);
            }

            var agora = DateTimeOffset.UtcNow;
            var pendencia = new PendenciaAgendamentoWhatsApp(
                c.NegocioId, conversa.Id, reserva.AgendamentoId!.Value, agora, agora.AddMinutes(10), nome);
            db.PendenciasAgendamentoWhatsApp.Add(pendencia);
            await db.SaveChangesAsync();
            return new Preparada(pendencia.Id, reserva.AgendamentoId.Value, conversa.Id);
        });
    }

    private Task<Agendamento> AgendamentoAsync(Guid id) =>
        Fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking().Include(a => a.Servicos).SingleAsync(a => a.Id == id));

    private Task<PendenciaAgendamentoWhatsApp> PendenciaAsync(Guid id) =>
        Fabrica.NoBancoAsync(db => db.PendenciasAgendamentoWhatsApp.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == id));

    private Task<ConversaWhatsApp> ConversaAsync(Guid id) =>
        Fabrica.NoBancoAsync(db => db.ConversasWhatsApp.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == id));

    private Task<List<Cliente>> ClientesAsync(Guid negocioId) =>
        Fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().AsNoTracking().Where(x => x.NegocioId == negocioId).ToListAsync());

    private Task<int> ReservasAtivasAsync(Guid profissionalId) =>
        Fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().CountAsync(a =>
            a.ProfissionalId == profissionalId && (a.Status == StatusAgendamento.Reservado || a.Status == StatusAgendamento.Agendado)));

    private static async Task<Guid> ClienteQualquerAsync(Plataforma.Infraestrutura.Persistencia.PlataformaDbContext db, Guid negocioId)
    {
        var cliente = Cliente.Criar(negocioId, "Cliente do forçado", TelefoneE164.Criar("+5571955554444"));
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return cliente.Id;
    }

    private Task DefinirEmailDoProfissionalAsync(Guid profissionalId, string email) =>
        Fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET email = {email} WHERE id = {profissionalId}");
            return 0;
        });

    private Task LigarWhatsAppDeConfirmacaoAsync(Guid negocioId) =>
        Fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE negocios SET whats_app_ativo_para_confirmacoes = true WHERE id = {negocioId}");
            return 0;
        });

    private Task SuspenderAssinaturaAsync(Guid negocioId) =>
        Fabrica.NoBancoAsync(async db =>
        {
            var plano = await db.Planos.FirstAsync(p => p.Nome == "Casa Cheia");
            var agora = DateTimeOffset.UtcNow;
            var assinatura = ServicoAssinatura.IniciarTeste(negocioId, plano, Periodicidade.Mensal, "teste", agora.AddDays(-45));
            ServicoAssinatura.AtualizarPorTempo(assinatura, agora);
            assinatura.Estado.Should().Be(EstadoAssinatura.Suspensa);
            db.Assinaturas.Add(assinatura);
            return await db.SaveChangesAsync();
        });
}

internal static class TuplaDeTarefas
{
    public static async Task<(T1, T2)> Pair<T1, T2>(this (Task<T1>, Task<T2>) tarefas)
    {
        await Task.WhenAll(tarefas.Item1, tarefas.Item2);
        return (tarefas.Item1.Result, tarefas.Item2.Result);
    }
}

/// <summary>Com a outbox ligada: a confirmação do agente enfileira o aviso ao profissional e o e-mail do cliente, nunca o WhatsApp dele.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AgendamentoPeloAgenteOutboxTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public AgendamentoPeloAgenteOutboxTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(new Dictionary<string, string?>
        {
            ["Notificacoes:OutboxHabilitada"] = "true",
            ["Notificacoes:OutboxOperacionais"] = "true",
        });
    }

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    [Fact]
    public async Task Outbox_enfileira_profissional_e_email_do_cliente_mas_nunca_o_whatsapp_da_confirmacao()
    {
        var f = _ambiente.Fabrica;
        var c = await _ambiente.SemearAsync();
        var dia = AmbienteAgente.ProximaSegunda();
        var inicio = SemeadorDeComissoes.Local(dia, 10, 0);

        await f.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE negocios SET whats_app_ativo_para_confirmacoes = true, whats_app_aviso_profissional = true WHERE id = {c.NegocioId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET email = 'beto@teste.com', telefone = '+5571966665555' WHERE id = {c.Beto}");
            db.Clientes.Add(Cliente.Criar(c.NegocioId, "Com Email", TelefoneE164.Criar("+5571988887777"), OrigemCliente.Painel, "cliente@teste.com"));
            return await db.SaveChangesAsync();
        });

        Guid pendenciaId;
        using (var escopo = Escopo(f, c.NegocioId))
        {
            var servico = escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>();
            var reserva = await servico.CriarReservaAgenteAsync(new CriarReservaAgente(c.Beto, c.Corte, inicio));
            reserva.Sucesso.Should().BeTrue(reserva.MensagemErro);

            var db = escopo.ServiceProvider.GetRequiredService<Plataforma.Infraestrutura.Persistencia.PlataformaDbContext>();
            var instancia = await db.InstanciasWhatsApp.FirstAsync();
            var conversa = new ConversaWhatsApp(c.NegocioId, instancia.Id, "+5571988887777", DateTimeOffset.UtcNow);
            db.ConversasWhatsApp.Add(conversa);
            var pendencia = new PendenciaAgendamentoWhatsApp(
                c.NegocioId, conversa.Id, reserva.AgendamentoId!.Value, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10));
            db.PendenciasAgendamentoWhatsApp.Add(pendencia);
            await db.SaveChangesAsync();
            pendenciaId = pendencia.Id;
        }

        using (var escopo = Escopo(f, c.NegocioId))
        {
            var r = await escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>()
                .ConfirmarReservaAgenteAsync(new ConfirmarReservaAgente(pendenciaId, "+5571988887777", null, Guid.NewGuid()));
            r.Sucesso.Should().BeTrue();
        }

        var fila = await f.NoBancoAsync(db => db.NotificacoesPendentes.IgnoreQueryFilters().Where(n => n.NegocioId == c.NegocioId).ToListAsync());
        fila.Where(n => n.Tipo == TipoNotificacao.Confirmacao).Select(n => n.Canal)
            .Should().BeEquivalentTo([CanalNotificacao.Email], "a confirmação ao cliente sai na conversa; o e-mail só porque o cadastro tem");
        fila.Where(n => n.Tipo == TipoNotificacao.ProfissionalNovo).Select(n => n.Canal)
            .Should().BeEquivalentTo([CanalNotificacao.Email, CanalNotificacao.WhatsApp], "o profissional continua avisado pelos canais dele");
    }

    [Fact]
    public async Task Cancelamento_avisando_o_cliente_do_agente_sem_email_enfileira_o_whatsapp()
    {
        var f = _ambiente.Fabrica;
        var c = await _ambiente.SemearAsync();
        var inicio = SemeadorDeComissoes.Local(AmbienteAgente.ProximaSegunda(), 10, 0);
        await f.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE negocios SET whats_app_ativo_para_confirmacoes = true WHERE id = {c.NegocioId}");
            return 0;
        });

        Guid agendamentoId;
        using (var escopo = Escopo(f, c.NegocioId))
        {
            var servico = escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>();
            var reserva = await servico.CriarReservaAgenteAsync(new CriarReservaAgente(c.Beto, c.Corte, inicio));
            reserva.Sucesso.Should().BeTrue(reserva.MensagemErro);
            agendamentoId = reserva.AgendamentoId!.Value;

            var db = escopo.ServiceProvider.GetRequiredService<Plataforma.Infraestrutura.Persistencia.PlataformaDbContext>();
            var instancia = await db.InstanciasWhatsApp.FirstAsync();
            var conversa = new ConversaWhatsApp(c.NegocioId, instancia.Id, "+5571988887777", DateTimeOffset.UtcNow);
            db.ConversasWhatsApp.Add(conversa);
            var pendencia = new PendenciaAgendamentoWhatsApp(
                c.NegocioId, conversa.Id, agendamentoId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), "Maria");
            db.PendenciasAgendamentoWhatsApp.Add(pendencia);
            await db.SaveChangesAsync();
            (await servico.ConfirmarReservaAgenteAsync(new ConfirmarReservaAgente(pendencia.Id, "+5571988887777", null, Guid.NewGuid()))).Sucesso.Should().BeTrue();
        }

        using (var escopo = Escopo(f, c.NegocioId))
            (await escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>().CancelarAvisandoClienteAsync(agendamentoId)).Should().BeTrue();

        var fila = await f.NoBancoAsync(db => db.NotificacoesPendentes.IgnoreQueryFilters().Where(n => n.NegocioId == c.NegocioId).ToListAsync());
        fila.Where(n => n.Tipo == TipoNotificacao.Cancelamento).Select(n => n.Canal)
            .Should().BeEquivalentTo([CanalNotificacao.WhatsApp], "cliente sem e-mail: o cancelamento sai pelo WhatsApp");
    }

    private static IServiceScope Escopo(Plataforma.Testes.Integracao.Infraestrutura.PlataformaWebApplicationFactory f, Guid negocioId)
    {
        var escopo = f.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioId);
        return escopo;
    }
}
