using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Cupons;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// Corridas da transferência e da confirmação de reserva (B2/B2.1). A ordem é coordenada de forma determinística: o escopo
/// "lento" lê o agendamento (como a requisição que já carregou e ainda vai gravar), outro escopo grava a operação concorrente
/// e só então o lento tenta gravar. O estado final é sempre conferido no banco, por um contexto novo.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ConcorrenciaTransferenciaConfirmacaoTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public ConcorrenciaTransferenciaConfirmacaoTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    // ---------------------------------------------------------------- transferência

    public static TheoryData<string, StatusAgendamento> Concorrentes => new()
    {
        { "cancelar_painel", StatusAgendamento.Cancelado },
        { "cancelar_publico", StatusAgendamento.Cancelado },
        { "iniciar", StatusAgendamento.EmAtendimento },
        { "faltou", StatusAgendamento.Faltou },
        { "concluir", StatusAgendamento.Concluido },
    };

    [Theory]
    [MemberData(nameof(Concorrentes))]
    public async Task Transferencia_que_leu_antes_nao_move_agendamento_que_mudou_de_status(string concorrente, StatusAgendamento esperado)
    {
        var c = await ArranjarAsync();
        var id = await SemearAgendadoAsync(c);

        using var lento = Escopo(c.NegocioId);
        await LerAsync(lento, id);
        await NoOutroEscopoAsync(c.NegocioId, s => concorrente switch
        {
            "cancelar_painel" => s.CancelarAsync(id),
            "cancelar_publico" => s.CancelarPelaGestaoPublicaAsync(id),
            "iniciar" => s.IniciarAtendimentoAsync(id),
            "faltou" => s.MarcarFaltouAsync(id),
            _ => s.MarcarConcluidoAsync(id),
        });

        var resultado = await Servico(lento).TransferirAsync(id, c.Agenda.ProfissionalId, c.Destino);

        resultado.Sucesso.Should().BeFalse();
        resultado.Conflito.Should().BeTrue();
        var estado = await EstadoAsync(id);
        estado.Status.Should().Be(esperado);
        estado.ProfissionalId.Should().Be(c.Agenda.ProfissionalId);
        AvisosPara(EmailDestino).Should().Be(0);
    }

    [Fact]
    public async Task Transferencia_que_leu_antes_da_remarcacao_valida_o_expediente_no_horario_atual()
    {
        var c = await ArranjarAsync(fimDestino: new TimeOnly(12, 0)); // o destino só trabalha de manhã
        var id = await SemearAgendadoAsync(c);
        var novoInicio = SemeadorDeComissoes.Local(c.Segunda, 16, 0);

        using var lento = Escopo(c.NegocioId);
        await LerAsync(lento, id);
        await NoOutroEscopoAsync(c.NegocioId, async s => (await s.MoverAsync(id, novoInicio)).Sucesso.Should().BeTrue());

        var resultado = await Servico(lento).TransferirAsync(id, c.Agenda.ProfissionalId, c.Destino);

        resultado.Sucesso.Should().BeFalse();
        resultado.MensagemErro.Should().Contain("expediente");
        var estado = await EstadoAsync(id);
        estado.ProfissionalId.Should().Be(c.Agenda.ProfissionalId);
        estado.Inicio.Should().Be(novoInicio, "a remarcação concorrente fica preservada");
        AvisosPara(EmailDestino).Should().Be(0);
    }

    [Fact]
    public async Task Duas_transferencias_concorrentes_so_a_primeira_vale_e_so_o_vencedor_e_avisado()
    {
        var c = await ArranjarAsync();
        var terceiro = await CriarDestinoAsync(c, "Terceiro", "terceiro.b21@teste.com");
        var id = await SemearAgendadoAsync(c);

        using var lento = Escopo(c.NegocioId);
        await LerAsync(lento, id);
        await NoOutroEscopoAsync(c.NegocioId, async s =>
            (await s.TransferirAsync(id, c.Agenda.ProfissionalId, terceiro)).Sucesso.Should().BeTrue());

        var resultado = await Servico(lento).TransferirAsync(id, c.Agenda.ProfissionalId, c.Destino);

        resultado.Sucesso.Should().BeFalse();
        resultado.Conflito.Should().BeTrue();
        (await EstadoAsync(id)).ProfissionalId.Should().Be(terceiro);
        AvisosPara("terceiro.b21@teste.com").Should().Be(1);
        AvisosPara(EmailDestino).Should().Be(0);
    }

    [Fact]
    public async Task Transferencia_sem_corrida_continua_funcionando_e_avisa_o_destino_uma_vez()
    {
        var c = await ArranjarAsync();
        var id = await SemearAgendadoAsync(c);

        using var escopo = Escopo(c.NegocioId);
        (await Servico(escopo).TransferirAsync(id, c.Agenda.ProfissionalId, c.Destino)).Sucesso.Should().BeTrue();

        (await EstadoAsync(id)).ProfissionalId.Should().Be(c.Destino);
        AvisosPara(EmailDestino).Should().Be(1);
    }

    [Fact]
    public async Task Transferencia_nao_alcanca_agendamento_nem_profissional_de_outro_negocio()
    {
        var c = await ArranjarAsync();
        var outro = await ArranjarAsync();
        var id = await SemearAgendadoAsync(c);

        // Destino de outro negócio.
        using (var escopo = Escopo(c.NegocioId))
            (await Servico(escopo).TransferirAsync(id, c.Agenda.ProfissionalId, outro.Destino)).Sucesso.Should().BeFalse();
        // Agendamento de outro negócio.
        using (var escopo = Escopo(outro.NegocioId))
            (await Servico(escopo).TransferirAsync(id, c.Agenda.ProfissionalId, outro.Destino)).MensagemErro.Should().Be("Agendamento não encontrado.");

        (await EstadoAsync(id)).ProfissionalId.Should().Be(c.Agenda.ProfissionalId);
    }

    // ---------------------------------------------------------------- confirmação

    [Fact]
    public async Task Confirmacao_publica_que_leu_antes_do_job_nao_ressuscita_reserva_expirada()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, comCliente: false);

        using var lento = Escopo(c.NegocioId);
        await LerAsync(lento, id);
        await VencerEExpirarPeloJobAsync(id);

        var resultado = await Servico(lento).ConfirmarReservaPublicaAsync(Confirmacao(id, "71988887777", "expirada@teste.com"));

        resultado.Sucesso.Should().BeFalse();
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Expirado);
        AvisosPara("expirada@teste.com").Should().Be(0);
    }

    [Fact]
    public async Task Confirmacao_publica_que_leu_antes_do_job_com_o_horario_ja_tomado_e_recusada_sem_excecao()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, comCliente: false);

        using var lento = Escopo(c.NegocioId);
        await LerAsync(lento, id);
        await VencerEExpirarPeloJobAsync(id);
        var outro = await _fabrica.SemearAgendamentoAsync(c.NegocioId, c.Agenda.ProfissionalId, c.Inicio, servicoId: c.Agenda.ServicoId);

        var resultado = await Servico(lento).ConfirmarReservaPublicaAsync(Confirmacao(id, "71988886666", "tomado@teste.com"));

        resultado.Sucesso.Should().BeFalse();
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Expirado);
        (await EstadoAsync(outro)).Status.Should().Be(StatusAgendamento.Agendado);
        AvisosPara("tomado@teste.com").Should().Be(0);
    }

    [Fact]
    public async Task Confirmacao_do_painel_que_leu_antes_do_cancelamento_nao_ressuscita_cancelada()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, comCliente: true);

        using var lento = Escopo(c.NegocioId);
        await LerAsync(lento, id);
        await NoOutroEscopoAsync(c.NegocioId, s => s.CancelarAsync(id));

        var resultado = await Servico(lento).ConfirmarReservaAsync(id);

        resultado.Sucesso.Should().BeFalse();
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Cancelado);
    }

    [Fact]
    public async Task Confirmacao_publica_que_leu_antes_do_cancelamento_da_exclusao_nao_ressuscita_nem_avisa()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, comCliente: false);

        using var lento = Escopo(c.NegocioId);
        await LerAsync(lento, id);
        await NoOutroEscopoAsync(c.NegocioId, s => s.CancelarAvisandoClienteAsync(id));

        var resultado = await Servico(lento).ConfirmarReservaPublicaAsync(Confirmacao(id, "71988885555", "exclusao@teste.com"));

        resultado.Sucesso.Should().BeFalse();
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Cancelado);
        AvisosPara("exclusao@teste.com").Should().Be(0);
    }

    [Fact]
    public async Task Confirmacao_repetida_pelo_mesmo_cliente_e_idempotente_sem_novo_aviso_nem_novo_uso_de_cupom()
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c, "B21DUPLO");
        var id = await SemearReservaAsync(c, comCliente: false);

        using var lento = Escopo(c.NegocioId);
        await LerAsync(lento, id);
        await NoOutroEscopoAsync(c.NegocioId, async s =>
            (await s.ConfirmarReservaPublicaAsync(Confirmacao(id, "71988881111", "mesmo@teste.com", "B21DUPLO"))).Sucesso.Should().BeTrue());
        var clienteDaPrimeira = await ClienteAsync(id);

        var repetida = await Servico(lento).ConfirmarReservaPublicaAsync(Confirmacao(id, "71988881111", "mesmo@teste.com", "B21DUPLO"));
        using (var outroPedido = Escopo(c.NegocioId))
            (await Servico(outroPedido).ConfirmarReservaPublicaAsync(Confirmacao(id, "71988881111", "mesmo@teste.com", "B21DUPLO")))
                .Sucesso.Should().BeTrue("uma nova tentativa sequencial também é idempotente");

        repetida.Sucesso.Should().BeTrue();
        repetida.AgendamentoId.Should().Be(id);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Agendado);
        (await ClienteAsync(id)).Should().Be(clienteDaPrimeira);
        AvisosPara("mesmo@teste.com").Should().Be(1);
        (await _fabrica.NoBancoAsync(db => db.Cupons.IgnoreQueryFilters().Where(x => x.Id == cupomId).Select(x => x.UsosAtuais).SingleAsync()))
            .Should().Be(1);
    }

    [Fact]
    public async Task Segunda_confirmacao_com_outro_cliente_e_recusada_e_nao_troca_o_cliente()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, comCliente: false);

        using var lento = Escopo(c.NegocioId);
        await LerAsync(lento, id);
        await NoOutroEscopoAsync(c.NegocioId, async s =>
            (await s.ConfirmarReservaPublicaAsync(Confirmacao(id, "71988881111", "primeiro@teste.com"))).Sucesso.Should().BeTrue());
        var clienteDaPrimeira = await ClienteAsync(id);

        var segunda = await Servico(lento).ConfirmarReservaPublicaAsync(Confirmacao(id, "71988882222", "segundo@teste.com"));

        segunda.Sucesso.Should().BeFalse();
        (await ClienteAsync(id)).Should().Be(clienteDaPrimeira);
        AvisosPara("primeiro@teste.com").Should().Be(1);
        AvisosPara("segundo@teste.com").Should().Be(0);
    }

    [Fact]
    public async Task Violacao_da_exclusion_constraint_na_confirmacao_vira_409_com_proximos_horarios_e_nunca_500()
    {
        var c = await ArranjarAsync();
        var publica = await SemearReservaAsync(c, comCliente: false);
        var doPainel = await SemearReservaAsync(c, comCliente: true, inicio: SemeadorDeComissoes.Local(c.Segunda, 14, 0));

        // Com a releitura sob trava, a corrida que gerava o 23P01 não chega mais ao UPDATE; a falha é injetada no banco
        // de teste para provar o tratamento defensivo (o mesmo SQLSTATE que a constraint levantaria).
        await using var falha = await InjetarViolacaoDeExclusaoAsync(publica, doPainel);

        using (var escopo = Escopo(c.NegocioId))
        {
            var resultado = await Servico(escopo).ConfirmarReservaPublicaAsync(Confirmacao(publica, "71988884444", "conflito@teste.com"));
            resultado.Sucesso.Should().BeFalse();
            resultado.Conflito.Should().BeTrue();
            resultado.ProximosHorariosLivres.Should().NotBeNull();
        }

        var http = await c.Admin.PostAsync($"/painel/agendamentos/{doPainel}/confirmar", null);
        http.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = await http.Content.ReadAsStringAsync();
        JsonDocument.Parse(corpo).RootElement.TryGetProperty("proximosHorariosLivres", out _).Should().BeTrue();
        corpo.Should().NotContainAny("23P01", "exclusion", "Npgsql", "constraint");

        (await EstadoAsync(publica)).Status.Should().Be(StatusAgendamento.Reservado);
        (await EstadoAsync(doPainel)).Status.Should().Be(StatusAgendamento.Reservado);
        AvisosPara("conflito@teste.com").Should().Be(0);
    }

    [Fact]
    public async Task Job_de_expiracao_concorrente_espera_a_trava_e_nao_altera_reserva_ja_confirmada()
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, comCliente: true);
        await _fabrica.NoBancoAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE agendamentos SET reservado_ate = now() - interval '1 minute' WHERE id = {id}"));

        // Paralelismo real: uma confirmação segura a linha e o job dispara em outra conexão.
        await using var conexao = new NpgsqlConnection(_postgres.ConnectionString);
        await conexao.OpenAsync();
        await using var transacao = await conexao.BeginTransactionAsync();
        await using (var confirma = new NpgsqlCommand("UPDATE agendamentos SET status = 'Agendado', reservado_ate = NULL WHERE id = @id", conexao, transacao))
        {
            confirma.Parameters.AddWithValue("id", id);
            await confirma.ExecuteNonQueryAsync();
        }

        var job = Task.Run(async () =>
        {
            using var escopo = _fabrica.Services.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<JobExpirarReservas>().ExecutarAsync();
        });

        // Sinal determinístico vindo do próprio banco: o job está parado na trava da linha.
        (await EsperarAlguemBloqueadoAsync(job)).Should().BeTrue("o job precisa disputar a mesma linha");
        await transacao.CommitAsync();
        await job;

        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Agendado);
    }

    [Fact]
    public async Task Confirmacao_nao_alcanca_reserva_de_outro_negocio()
    {
        var c = await ArranjarAsync();
        var outro = await ArranjarAsync();
        var id = await SemearReservaAsync(c, comCliente: true);

        using (var escopo = Escopo(outro.NegocioId))
        {
            (await Servico(escopo).ConfirmarReservaPublicaAsync(Confirmacao(id, "71988883333", null))).Sucesso.Should().BeFalse();
            (await Servico(escopo).ConfirmarReservaAsync(id)).Sucesso.Should().BeFalse();
        }

        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Reservado);
    }

    // ---------------------------------------------------------------- apoio

    private const string EmailDestino = "destino.b21@teste.com";

    private sealed record Cenario(Guid NegocioId, HttpClient Admin, SemeadorDeAgenda.CenarioDeAgenda Agenda, Guid Destino, DateOnly Segunda)
    {
        public DateTimeOffset Inicio => SemeadorDeComissoes.Local(Segunda, 10, 0);
    }

    private async Task<Cenario> ArranjarAsync(TimeOnly? fimDestino = null)
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var agenda = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);

        var c = new Cenario(negocioId, admin, agenda, Guid.Empty, segunda);
        return c with { Destino = await CriarDestinoAsync(c, "Destino", EmailDestino, fimDestino) };
    }

    private async Task<Guid> CriarDestinoAsync(Cenario c, string nome, string email, TimeOnly? fim = null)
    {
        var id = await _fabrica.CriarProfissionalAsync(c.NegocioId, nome);
        await _fabrica.NoBancoAsync(async db =>
        {
            foreach (var dia in Enum.GetValues<DiaSemana>())
                db.HorariosTrabalho.Add(HorarioTrabalho.Criar(c.NegocioId, id, dia, new TimeOnly(9, 0), fim ?? new TimeOnly(18, 0)));
            db.ProfissionalServicos.Add(ProfissionalServico.Criar(c.NegocioId, id, c.Agenda.ServicoId));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET email = {email} WHERE id = {id}");
            return await db.SaveChangesAsync();
        });
        return id;
    }

    private Task<Guid> SemearAgendadoAsync(Cenario c) =>
        _fabrica.SemearAgendamentoAsync(c.NegocioId, c.Agenda.ProfissionalId, c.Inicio, servicoId: c.Agenda.ServicoId);

    private Task<Guid> SemearReservaAsync(Cenario c, bool comCliente, DateTimeOffset? inicio = null) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var itens = new[] { new ItemServicoAgendamento(c.Agenda.ServicoId, "Serviço de Teste", 50m, 30) };
            var reserva = Agendamento.CriarReserva(c.NegocioId, c.Agenda.ProfissionalId, comCliente ? c.Agenda.ClienteId : null,
                inicio ?? c.Inicio, itens, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));
            db.Agendamentos.Add(reserva);
            await db.SaveChangesAsync();
            return reserva.Id;
        });

    private Task<Guid> CriarCupomAsync(Cenario c, string codigo) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var cupom = Cupom.Criar(c.NegocioId, codigo, TipoDescontoCupom.Percentual, 10m);
            db.Cupons.Add(cupom);
            await db.SaveChangesAsync();
            return cupom.Id;
        });

    private static ConfirmarReservaPublica Confirmacao(Guid id, string telefone, string? email, string? cupom = null) =>
        new(id, "Cliente da Corrida", telefone, email, null, cupom, "127.0.0.1");

    private IServiceScope Escopo(Guid negocioId)
    {
        var escopo = _fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioId);
        return escopo;
    }

    private static IServicoAgendamentos Servico(IServiceScope escopo) => escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>();

    /// <summary>A leitura da requisição "lenta": a entidade fica rastreada no escopo dela, com o estado deste instante.</summary>
    private static Task LerAsync(IServiceScope escopo, Guid id) =>
        escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().Agendamentos.Include(a => a.Servicos).FirstAsync(a => a.Id == id);

    private async Task NoOutroEscopoAsync(Guid negocioId, Func<IServicoAgendamentos, Task> acao)
    {
        using var escopo = Escopo(negocioId);
        await acao(Servico(escopo));
    }

    private Task<Agendamento> EstadoAsync(Guid id) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == id));

    private async Task<Guid?> ClienteAsync(Guid id) => (await EstadoAsync(id)).ClienteId;

    private int AvisosPara(string email) => _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Count(e => e.Destinatario == email);

    private Task VencerEExpirarPeloJobAsync(Guid id) =>
        _fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE agendamentos SET reservado_ate = now() - interval '1 minute' WHERE id = {id}");
            using var escopo = _fabrica.Services.CreateScope();
            await escopo.ServiceProvider.GetRequiredService<JobExpirarReservas>().ExecutarAsync();
            return 0;
        });

    private async Task<bool> EsperarAlguemBloqueadoAsync(Task concorrente)
    {
        await using var conexao = new NpgsqlConnection(_postgres.ConnectionString);
        await conexao.OpenAsync();
        for (var i = 0; i < 2000 && !concorrente.IsCompleted; i++)
        {
            await using var consulta = new NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE NOT granted", conexao);
            if ((long)(await consulta.ExecuteScalarAsync())! > 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gatilho só no banco de teste: qualquer UPDATE nessas linhas falha com o SQLSTATE da exclusion constraint.
    /// DDL não aceita parâmetro no nome do objeto, então o SQL é montado aqui — só com valores gerados pelo próprio teste
    /// (um nome aleatório e GUIDs), nunca com entrada externa.
    /// </summary>
    private async Task<IAsyncDisposable> InjetarViolacaoDeExclusaoAsync(params Guid[] ids)
    {
        var nome = "b21_falha_" + Guid.NewGuid().ToString("N")[..8];
        var lista = string.Join(", ", ids.Select(i => "'" + i.ToString("D") + "'"));
        var criarFuncao = "CREATE FUNCTION " + nome
            + "() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'simulada' USING ERRCODE = '23P01'; END $$";
        var criarGatilho = "CREATE TRIGGER " + nome + " BEFORE UPDATE ON agendamentos FOR EACH ROW WHEN (OLD.id IN (" + lista
            + ")) EXECUTE FUNCTION " + nome + "()";
        var removerGatilho = "DROP TRIGGER IF EXISTS " + nome + " ON agendamentos";
        var removerFuncao = "DROP FUNCTION IF EXISTS " + nome + "()";

        await _fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(criarFuncao);
            await db.Database.ExecuteSqlRawAsync(criarGatilho);
            return 0;
        });
        return new Remocao(() => _fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(removerGatilho);
            await db.Database.ExecuteSqlRawAsync(removerFuncao);
            return 0;
        }));
    }

    private sealed class Remocao(Func<Task> remover) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await remover();
    }
}
