using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Cadastros;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Testes.Integracao.Infraestrutura;
using Plataforma.Testes.Integracao.Notificacoes.Outbox;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// B2.2 — as duas corridas que a auditoria B2 deixou sem veredito: (1) cancelamento pelo painel contra outra transição do mesmo
/// agendamento e (2) exclusão de profissional contra criação, remarcação ou transferência de agendamento para ele.
/// A ordem é coordenada sem sleeps: um gatilho de teste faz a gravação "lenta" esperar numa trava de aviso (advisory lock) que o
/// teste segura; o teste só segue quando o <c>pg_locks</c> mostra a espera, deixa a operação concorrente avançar até terminar ou
/// também bloquear, e só então solta a trava. As asserções valem para qualquer vencedor (invariantes do estado final).
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ConcorrenciaCancelamentoExclusaoTestes(PostgresContainerFixture postgres) : OutboxTestesBase(postgres)
{
    private static readonly StatusNotificacao Pendente = StatusNotificacao.Pendente;

    // =============================================================== 1. cancelamento pelo painel

    public static TheoryData<string> OperacoesConcorrentesAoCancelamento => new() { "transferir", "cancelar_publico", "mover" };

    [Theory]
    [MemberData(nameof(OperacoesConcorrentesAoCancelamento))]
    public async Task Cancelamento_do_painel_gravando_e_outra_operacao_concorrente_deixam_estado_e_mensagens_consistentes(string concorrente)
    {
        var c = await ArranjarAsync();
        var destino = await CriarDestinoAsync(c, "Destino", false);
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));
        // O cancelamento para na invalidação das mensagens pendentes, depois de ler o agendamento e antes de gravar o status: é a janela
        // da requisição que já leu. (Parar no UPDATE do próprio agendamento não serve: o gatilho de linha já travaria a linha.)
        await using var pausa = await Pausa.CriarAsync(this, "notificacoes_pendentes", "UPDATE", $"NEW.agendamento_id = '{id}'");

        var painel = Task.Run(() => ComServicoAsync(c.NegocioId, s => s.CancelarAsync(id)));
        await pausa.EsperarAsync(painel, bloqueios: 1);
        painel.IsCompleted.Should().BeFalse("o cancelamento precisa estar parado na gravação para a corrida existir");

        var outra = Task.Run<(bool Sucesso, string Detalhe)>(async () =>
        {
            try
            {
                switch (concorrente)
                {
                    case "transferir":
                        var t = await ComServicoAsync(c.NegocioId, s => s.TransferirAsync(id, c.ProfissionalId, destino));
                        return (t.Sucesso, t.MensagemErro ?? "");
                    case "mover":
                        var m = await ComServicoAsync(c.NegocioId, s => s.MoverAsync(id, Hora(c, 14)));
                        return (m.Sucesso, m.MensagemErro ?? "");
                    default:
                        var p = await ComServicoAsync(c.NegocioId, s => s.CancelarPelaGestaoPublicaAsync(id));
                        return (p == ResultadoCancelamentoPublico.Cancelado, p.ToString());
                }
            }
            catch (InvalidOperationException excecao)
            {
                return (false, excecao.Message); // a transição de domínio recusou o estado atual
            }
        });
        await pausa.EsperarAsync(outra, bloqueios: 2);
        await pausa.LiberarAsync();

        var cancelouNoPainel = await painel;
        var resultadoOutra = await outra;

        cancelouNoPainel.Should().BeTrue();
        if (concorrente == "cancelar_publico")
            resultadoOutra.Sucesso.Should().BeFalse("só um dos dois cancelamentos vale; o outro enxerga o que o primeiro gravou");
        await ConferirCancelamentoConsistenteAsync(id);
    }

    public static TheoryData<string> TransicoesAntesDoCancelamento => new() { "transferir", "cancelar_publico", "concluir", "iniciar", "faltou" };

    /// <summary>
    /// A requisição do painel já tinha lido o agendamento (entidade rastreada no escopo dela) quando a outra transição gravou: o
    /// cancelamento decide sobre o estado atual do banco, nunca sobre a cópia antiga.
    /// </summary>
    [Theory]
    [MemberData(nameof(TransicoesAntesDoCancelamento))]
    public async Task Cancelamento_do_painel_que_leu_antes_decide_sobre_o_estado_atual(string antes)
    {
        var c = await ArranjarAsync();
        var destino = await CriarDestinoAsync(c, "Destino", false);
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        using var lento = EscopoDoNegocio(c.NegocioId);
        await lento.ServiceProvider.GetRequiredService<Plataforma.Infraestrutura.Persistencia.PlataformaDbContext>()
            .Agendamentos.Include(a => a.Servicos).FirstAsync(a => a.Id == id);

        await ComServicoAsync(c.NegocioId, async s =>
        {
            switch (antes)
            {
                case "transferir": (await s.TransferirAsync(id, c.ProfissionalId, destino)).Sucesso.Should().BeTrue(); break;
                case "cancelar_publico": (await s.CancelarPelaGestaoPublicaAsync(id)).Should().Be(ResultadoCancelamentoPublico.Cancelado); break;
                case "concluir": (await s.MarcarConcluidoAsync(id)).Should().BeTrue(); break;
                case "iniciar": (await s.IniciarAtendimentoAsync(id)).Should().BeTrue(); break;
                default: (await s.MarcarFaltouAsync(id)).Should().BeTrue(); break;
            }

            return 0;
        });

        var servicoLento = lento.ServiceProvider.GetRequiredService<IServicoAgendamentos>();
        var estadoAntes = await EstadoAsync(id);

        if (antes is "cancelar_publico" or "concluir")
        {
            // Já cancelado ou concluído: o domínio recusa, como recusaria uma requisição nova (nada é sobrescrito).
            await servicoLento.Invoking(s => s.CancelarAsync(id)).Should().ThrowAsync<InvalidOperationException>();
            (await EstadoAsync(id)).Status.Should().Be(estadoAntes.Status);
            return;
        }

        // Transferido, iniciado ou com falta: cancelar continua permitido (comportamento deliberado do painel), agora com o estado atual.
        (await servicoLento.CancelarAsync(id)).Should().BeTrue();
        var final = await EstadoAsync(id);
        final.Status.Should().Be(StatusAgendamento.Cancelado);
        final.ProfissionalId.Should().Be(estadoAntes.ProfissionalId, "o profissional gravado pela transferência fica");
        await ConferirCancelamentoConsistenteAsync(id);
    }

    [Fact]
    public async Task Cancelamento_do_painel_sem_corrida_invalida_pendentes_e_avisa_o_profissional_uma_vez_por_canal()
    {
        var c = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        (await ComServicoAsync(c.NegocioId, s => s.CancelarAsync(id))).Should().BeTrue();

        await ConferirCancelamentoConsistenteAsync(id);
        (await ItensDoAgendamentoAsync(id)).Count(i => i.Tipo == TipoNotificacao.ProfissionalCancelado).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Cancelamento_nao_alcanca_agendamento_de_outro_negocio()
    {
        var c = await ArranjarAsync();
        var outro = await ArranjarAsync();
        var id = await CriarAgendamentoAsync(c, Hora(c, 10));

        (await ComServicoAsync(outro.NegocioId, s => s.CancelarAsync(id))).Should().BeFalse();

        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Agendado);
    }

    private async Task ConferirCancelamentoConsistenteAsync(Guid id)
    {
        var estado = await EstadoAsync(id);
        estado.Status.Should().Be(StatusAgendamento.Cancelado);

        var itens = await ItensDoAgendamentoAsync(id);
        itens.Where(i => i.Status == Pendente
                && (i.Tipo == TipoNotificacao.Confirmacao || i.Tipo == TipoNotificacao.Lembrete60
                    || i.Tipo == TipoNotificacao.ProfissionalNovo || i.Tipo == TipoNotificacao.ProfissionalRemarcado))
            .Should().BeEmpty("mensagem de um agendamento cancelado é obsoleta e não pode sair depois");

        var avisos = itens.Where(i => i.Tipo == TipoNotificacao.ProfissionalCancelado).ToList();
        avisos.Select(i => i.Canal).Should().OnlyHaveUniqueItems("o aviso de cancelamento sai no máximo uma vez por canal");
        avisos.Should().OnlyContain(i => i.ProfissionalId == estado.ProfissionalId, "quem é avisado é o profissional que o agendamento tem de fato");
    }

    // =============================================================== 2. exclusão de profissional

    public static TheoryData<string, bool> Operacoes => new()
    {
        { "criar", true }, { "criar", false }, { "transferir", true }, { "transferir", false }, { "mover", true },
    };

    /// <summary>A operação sobre a agenda para no meio da gravação; a exclusão do profissional chega nesse intervalo.</summary>
    [Theory]
    [MemberData(nameof(Operacoes))]
    public async Task Operacao_na_agenda_gravando_e_exclusao_do_profissional_concorrente_nao_deixam_agendamento_em_profissional_excluido(
        string operacao, bool comHistorico)
    {
        var a = await PrepararAsync(operacao, comHistorico);
        await using var pausa = await Pausa.CriarAsync(this, "agendamentos", a.EventoDaOperacao, a.CondicaoDaOperacao);

        var op = Task.Run(a.Operar);
        await pausa.EsperarAsync(op, bloqueios: 1);
        op.IsCompleted.Should().BeFalse();

        var exclusao = Task.Run(() => ExcluirAsync(a.C.NegocioId, a.Profissional));
        await pausa.EsperarAsync(exclusao, bloqueios: 2);
        await pausa.LiberarAsync();

        await ConferirExclusaoConsistenteAsync(a, await op, await exclusao);
    }

    /// <summary>A exclusão para no meio da gravação; a operação sobre a agenda chega nesse intervalo.</summary>
    [Theory]
    [MemberData(nameof(Operacoes))]
    public async Task Exclusao_do_profissional_gravando_e_operacao_na_agenda_concorrente_nao_deixam_agendamento_em_profissional_excluido(
        string operacao, bool comHistorico)
    {
        var a = await PrepararAsync(operacao, comHistorico);
        var (evento, condicao) = comHistorico
            ? ("UPDATE", $"NEW.id = '{a.Profissional}' AND NEW.excluido")
            : ("DELETE", $"OLD.id = '{a.Profissional}'");
        await using var pausa = await Pausa.CriarAsync(this, "profissionais", evento, condicao);

        var exclusao = Task.Run(() => ExcluirAsync(a.C.NegocioId, a.Profissional));
        await pausa.EsperarAsync(exclusao, bloqueios: 1);
        exclusao.IsCompleted.Should().BeFalse();

        var op = Task.Run(a.Operar);
        await pausa.EsperarAsync(op, bloqueios: 2);
        await pausa.LiberarAsync();

        await ConferirExclusaoConsistenteAsync(a, await op, await exclusao);
    }

    /// <summary>Sem corrida: excluído antes, nada novo entra na agenda dele.</summary>
    [Theory]
    [MemberData(nameof(Operacoes))]
    public async Task Profissional_excluido_nao_recebe_criacao_remarcacao_nem_transferencia(string operacao, bool comHistorico)
    {
        var a = await PrepararAsync(operacao, comHistorico);

        var exclusao = await ExcluirAsync(a.C.NegocioId, a.Profissional);
        var op = await a.Operar();

        exclusao.Tipo.Should().NotBe(TipoResultadoExclusao.Bloqueado);
        op.Sucesso.Should().BeFalse();
        await ConferirExclusaoConsistenteAsync(a, op, exclusao);
    }

    [Fact]
    public async Task Exclusao_parada_num_negocio_nao_atrasa_agendamento_em_outro()
    {
        var c = await ArranjarAsync();
        var outro = await ArranjarAsync();
        var a = await PrepararAsync("criar", comHistorico: false, c);
        await using var pausa = await Pausa.CriarAsync(this, "profissionais", "DELETE", $"OLD.id = '{a.Profissional}'");

        var exclusao = Task.Run(() => ExcluirAsync(c.NegocioId, a.Profissional));
        await pausa.EsperarAsync(exclusao, bloqueios: 1);

        var emOutroNegocio = await ComServicoAsync(outro.NegocioId, s => s.CriarAsync(
            new CriarAgendamento(outro.ProfissionalId, outro.ClienteId, [outro.ServicoId], Hora(outro, 10), null)));

        emOutroNegocio.Sucesso.Should().BeTrue("a trava é por profissional: outro negócio não espera por ela");
        exclusao.IsCompleted.Should().BeFalse();
        await pausa.LiberarAsync();
        (await exclusao).Tipo.Should().Be(TipoResultadoExclusao.ApagadoDefinitivamente);
    }

    // ---------------------------------------------------------------- cenários da exclusão

    private sealed record Alvo(
        CenarioOutbox C, Guid Profissional, string EventoDaOperacao, string CondicaoDaOperacao, Func<Task<ResultadoAgendamento>> Operar);

    private async Task<Alvo> PrepararAsync(string operacao, bool comHistorico, CenarioOutbox? cenario = null)
    {
        var c = cenario ?? await ArranjarAsync();
        var passado = DateTimeOffset.UtcNow.AddDays(-10);

        switch (operacao)
        {
            case "criar":
                if (comHistorico)
                    await Fabrica.SemearAgendamentoAsync(c.NegocioId, c.ProfissionalId, passado, servicoId: c.ServicoId);
                return new Alvo(c, c.ProfissionalId, "INSERT", $"NEW.profissional_id = '{c.ProfissionalId}'", () =>
                    ComServicoAsync(c.NegocioId, s => s.CriarAsync(new CriarAgendamento(c.ProfissionalId, c.ClienteId, [c.ServicoId], Hora(c, 10), null))));

            case "transferir":
                var destino = await CriarDestinoAsync(c, "Destino", comHistorico);
                var id = await CriarAgendamentoAsync(c, Hora(c, 10));
                return new Alvo(c, destino, "UPDATE", $"NEW.id = '{id}' AND NEW.profissional_id = '{destino}'", () =>
                    ComServicoAsync(c.NegocioId, s => s.TransferirAsync(id, c.ProfissionalId, destino)));

            default: // mover: agendamento "Agendado" que já passou não conta como futuro e não impede a exclusão
                var antigo = await Fabrica.SemearAgendamentoAsync(c.NegocioId, c.ProfissionalId, passado, servicoId: c.ServicoId);
                return new Alvo(c, c.ProfissionalId, "UPDATE", $"NEW.id = '{antigo}'", () =>
                    ComServicoAsync(c.NegocioId, s => s.MoverAsync(antigo, Hora(c, 14))));
        }
    }

    private async Task ConferirExclusaoConsistenteAsync(Alvo a, ResultadoAgendamento operacao, ResultadoExclusao exclusao)
    {
        var profissional = await Fabrica.NoBancoAsync(db =>
            db.Profissionais.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p => p.Id == a.Profissional));
        var removido = profissional is null || profissional.Excluido;

        var agora = DateTimeOffset.UtcNow;
        var futuros = await Fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.ProfissionalId == a.Profissional && x.Inicio > agora
                && (x.Status == StatusAgendamento.Agendado || x.Status == StatusAgendamento.Reservado || x.Status == StatusAgendamento.EmAtendimento)));

        if (removido)
            futuros.Should().Be(0, "profissional excluído não pode ter agendamento ativo no futuro");
        (operacao.Sucesso && exclusao.Tipo != TipoResultadoExclusao.Bloqueado).Should().BeFalse(
            "ou a operação foi aceita e a exclusão bloqueada, ou a exclusão valeu e a operação foi recusada");
    }

    private async Task<ResultadoExclusao> ExcluirAsync(Guid negocioId, Guid profissionalId)
    {
        using var escopo = EscopoDoNegocio(negocioId);
        return await escopo.ServiceProvider.GetRequiredService<IGerenciadorProfissionais>().ExcluirAsync(profissionalId);
    }

    // ---------------------------------------------------------------- apoio

    private IServiceScope EscopoDoNegocio(Guid negocioId)
    {
        var escopo = Fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioId);
        return escopo;
    }

    private Task<Agendamento> EstadoAsync(Guid id) =>
        Fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == id));

    /// <summary>Profissional que executa o serviço do cenário, com expediente o dia todo; <paramref name="comHistorico"/> semeia um atendimento passado.</summary>
    private async Task<Guid> CriarDestinoAsync(CenarioOutbox c, string nome, bool comHistorico)
    {
        var id = await Fabrica.CriarProfissionalAsync(c.NegocioId, nome);
        await Fabrica.NoBancoAsync(async db =>
        {
            foreach (var dia in Enum.GetValues<DiaSemana>())
                db.HorariosTrabalho.Add(HorarioTrabalho.Criar(c.NegocioId, id, dia, new TimeOnly(9, 0), new TimeOnly(18, 0)));
            db.ProfissionalServicos.Add(ProfissionalServico.Criar(c.NegocioId, id, c.ServicoId));
            return await db.SaveChangesAsync();
        });

        if (comHistorico)
            await Fabrica.SemearAgendamentoAsync(c.NegocioId, id, DateTimeOffset.UtcNow.AddDays(-10), servicoId: c.ServicoId);
        return id;
    }

    /// <summary>
    /// Gatilho de teste (só neste banco) que faz a gravação que casa com a condição esperar numa trava de aviso segurada pelo teste.
    /// O SQL do gatilho é montado aqui só com valores gerados pelo próprio teste (chave aleatória e GUIDs).
    /// </summary>
    private sealed class Pausa : IAsyncDisposable
    {
        private readonly ConcorrenciaCancelamentoExclusaoTestes _teste;
        private readonly NpgsqlConnection _conexao;
        private readonly NpgsqlTransaction _transacao;
        private readonly string _nome;
        private readonly string _tabela;
        private bool _liberada;

        private Pausa(ConcorrenciaCancelamentoExclusaoTestes teste, NpgsqlConnection conexao, NpgsqlTransaction transacao, string nome, string tabela)
        {
            _teste = teste;
            _conexao = conexao;
            _transacao = transacao;
            _nome = nome;
            _tabela = tabela;
        }

        public static async Task<Pausa> CriarAsync(ConcorrenciaCancelamentoExclusaoTestes teste, string tabela, string eventos, string condicao)
        {
            var chave = Random.Shared.NextInt64(1, long.MaxValue);
            var nome = "b22_pausa_" + Guid.NewGuid().ToString("N")[..8];

            var conexao = new NpgsqlConnection(teste.PostgresConnectionString);
            await conexao.OpenAsync();
            var transacao = await conexao.BeginTransactionAsync();
            await using (var travar = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@chave)", conexao, transacao))
            {
                travar.Parameters.AddWithValue("chave", chave);
                await travar.ExecuteNonQueryAsync();
            }

            var funcao = "CREATE FUNCTION " + nome + "() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN PERFORM pg_advisory_xact_lock(" + chave
                + "); RETURN COALESCE(NEW, OLD); END $$";
            var gatilho = "CREATE TRIGGER " + nome + " BEFORE " + eventos + " ON " + tabela + " FOR EACH ROW WHEN (" + condicao
                + ") EXECUTE FUNCTION " + nome + "()";
            await teste.ExecutarSqlAsync(funcao);
            await teste.ExecutarSqlAsync(gatilho);
            return new Pausa(teste, conexao, transacao, nome, tabela);
        }

        /// <summary>
        /// Espera, olhando o banco, até a tarefa terminar ou haver pelo menos <paramref name="bloqueios"/> sessões esperando uma trava.
        /// </summary>
        public async Task EsperarAsync(Task tarefa, int bloqueios)
        {
            await using var consulta = new NpgsqlConnection(_teste.PostgresConnectionString);
            await consulta.OpenAsync();
            var limite = DateTime.UtcNow.AddSeconds(30);
            while (!tarefa.IsCompleted)
            {
                await using var comando = new NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE NOT granted", consulta);
                if ((long)(await comando.ExecuteScalarAsync())! >= bloqueios)
                    return;
                if (DateTime.UtcNow > limite)
                    throw new TimeoutException("A corrida não chegou ao ponto esperado.");
                await Task.Yield();
            }
        }

        public async Task LiberarAsync()
        {
            if (_liberada)
                return;
            _liberada = true;
            await _transacao.CommitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await LiberarAsync();
            await _transacao.DisposeAsync();
            await _conexao.DisposeAsync();
            await _teste.ExecutarSqlAsync("DROP TRIGGER IF EXISTS " + _nome + " ON " + _tabela);
            await _teste.ExecutarSqlAsync("DROP FUNCTION IF EXISTS " + _nome + "()");
        }
    }

    private string PostgresConnectionString => Postgres.ConnectionString;

    private Task ExecutarSqlAsync(string sql) => Fabrica.NoBancoAsync(db => db.Database.ExecuteSqlRawAsync(sql));
}
