using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Cupons;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Cupons;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// Limite de usos do cupom sob concorrência (B5/B5.1). Paralelismo real com coordenação pelo próprio banco: uma conexão
/// externa segura uma linha, o teste espera as transações aparecerem bloqueadas em <c>pg_locks</c> e só então solta.
/// O estado final é sempre lido num contexto novo. Reserva de R$ 50 e cupom de 10% → desconto de R$ 5.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ConcorrenciaCupomTestes : IAsyncLifetime
{
    private const decimal Preco = 50m;
    private const decimal Desconto = 5m;

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public ConcorrenciaCupomTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    // ---------------------------------------------------------------- limite e contador

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 2)]
    public async Task Duas_reservas_disputando_o_ultimo_uso_so_uma_ganha_o_desconto(int limite, int usosIniciais)
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "ULTIMO", limite, usosIniciais);
        var a = await SemearReservaAsync(c, 13);
        var b = await SemearReservaAsync(c, 14);

        // As duas confirmações chegam juntas à linha do cupom, que uma conexão externa segura.
        Task<ResultadoAgendamento> t1, t2;
        await using (var trava = await TravarLinhaAsync("cupons", cupomId))
        {
            t1 = ConfirmarAsync(c.NegocioId, a, "71966660001", "ultimo.a@teste.com", "ULTIMO");
            t2 = ConfirmarAsync(c.NegocioId, b, "71966660002", "ultimo.b@teste.com", "ULTIMO");
            (await EsperarBloqueadosAsync(2, t1, t2)).Should().BeTrue("as duas precisam disputar a linha do cupom");
        }

        var resultados = await Task.WhenAll(t1, t2);

        resultados.Should().OnlyContain(r => r.Sucesso, "cupom esgotado não impede a confirmação, só o desconto");
        var estados = new[] { await EstadoAsync(a), await EstadoAsync(b) };
        estados.Count(e => e.DescontoAplicado == Desconto).Should().Be(1);
        estados.Count(e => e.DescontoAplicado == 0 && e.CupomId == null).Should().Be(1);
        estados.Select(e => e.Total).Should().BeEquivalentTo([Preco - Desconto, Preco]);
        estados.Should().OnlyContain(e => e.Status == StatusAgendamento.Agendado);
        (await UsosAsync(cupomId)).Should().Be(limite);
        Avisos("ultimo.a@teste.com").Should().Be(1);
        Avisos("ultimo.b@teste.com").Should().Be(1);
    }

    [Fact]
    public async Task Dez_confirmacoes_simultaneas_com_limite_um_concedem_um_unico_desconto()
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "CARGA", limite: 1);
        var reservas = await SemearReservasAsync(c, 10);

        var resultados = await ConfirmarTodasJuntasAsync(c, reservas, "CARGA", "carga");

        resultados.Should().HaveCount(10).And.OnlyContain(r => r.Sucesso);
        var estados = await EstadosAsync(reservas);
        estados.Count(e => e.DescontoAplicado == Desconto).Should().Be(1);
        estados.Count(e => e.Total == Preco).Should().Be(9);
        (await UsosAsync(cupomId)).Should().Be(1);
        reservas.Select((_, i) => Avisos($"carga{i}@teste.com")).Should().OnlyContain(n => n == 1);
    }

    [Fact]
    public async Task Cupom_sem_limite_conta_todas_as_confirmacoes_simultaneas()
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "LIVRE", limite: null);
        var reservas = await SemearReservasAsync(c, 10);

        var resultados = await ConfirmarTodasJuntasAsync(c, reservas, "LIVRE", "livre");

        resultados.Should().OnlyContain(r => r.Sucesso);
        (await EstadosAsync(reservas)).Should().OnlyContain(e => e.DescontoAplicado == Desconto && e.Total == Preco - Desconto);
        (await UsosAsync(cupomId)).Should().Be(10, "nenhuma atualização do contador pode se perder");
    }

    [Fact]
    public async Task Cupom_ja_esgotado_confirma_sem_desconto_e_sem_mexer_no_contador()
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "CHEIO", limite: 2, usosIniciais: 2);
        var a = await SemearReservaAsync(c, 13);

        var resultado = await ConfirmarAsync(c.NegocioId, a, "71966661111", "cheio@teste.com", "CHEIO");

        resultado.Sucesso.Should().BeTrue();
        var estado = await EstadoAsync(a);
        (estado.Status, estado.CupomId, estado.DescontoAplicado, estado.Total).Should().Be((StatusAgendamento.Agendado, (Guid?)null, 0m, Preco));
        (await UsosAsync(cupomId)).Should().Be(2);
        Avisos("cheio@teste.com").Should().Be(1);
    }

    // ---------------------------------------------------------------- idempotência

    [Fact]
    public async Task Mesma_reserva_confirmada_ao_mesmo_tempo_e_repetida_depois_conta_um_uso()
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "MESMA", limite: 5);
        var a = await SemearReservaAsync(c, 13);

        Task<ResultadoAgendamento> t1, t2;
        await using (var trava = await TravarLinhaAsync("agendamentos", a))
        {
            t1 = ConfirmarAsync(c.NegocioId, a, "71966662222", "mesma@teste.com", "MESMA");
            t2 = ConfirmarAsync(c.NegocioId, a, "71966662222", "mesma@teste.com", "MESMA");
            (await EsperarBloqueadosAsync(2, t1, t2)).Should().BeTrue();
        }

        (await Task.WhenAll(t1, t2)).Should().OnlyContain(r => r.Sucesso && r.AgendamentoId == a);
        (await ConfirmarAsync(c.NegocioId, a, "71966662222", "mesma@teste.com", "MESMA")).Sucesso.Should().BeTrue("a repetição depois do sucesso também");

        var estado = await EstadoAsync(a);
        (estado.DescontoAplicado, estado.Total).Should().Be((Desconto, Preco - Desconto));
        (await UsosAsync(cupomId)).Should().Be(1);
        Avisos("mesma@teste.com").Should().Be(1);
    }

    // ---------------------------------------------------------------- desativação e limite concorrentes

    [Fact]
    public async Task Desativacao_gravada_durante_a_confirmacao_vale_para_ela()
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "DESLIGA", limite: 5);
        var a = await SemearReservaAsync(c, 13);

        Task<ResultadoAgendamento> confirmacao;
        await using (var desativacao = await AlterarCupomSemCommitAsync(cupomId, "ativo = false"))
        {
            confirmacao = ConfirmarAsync(c.NegocioId, a, "71966663333", "desliga@teste.com", "DESLIGA");
            (await EsperarBloqueadosAsync(1, confirmacao)).Should().BeTrue();
            await desativacao.ConfirmarAsync();
        }

        (await confirmacao).Sucesso.Should().BeTrue();
        (await EstadoAsync(a)).DescontoAplicado.Should().Be(0);
        (await UsosAsync(cupomId)).Should().Be(0);
    }

    [Fact]
    public async Task Reducao_do_limite_gravada_durante_a_confirmacao_vale_para_ela()
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "REDUZ", limite: 5, usosIniciais: 1);
        var a = await SemearReservaAsync(c, 13);

        Task<ResultadoAgendamento> confirmacao;
        await using (var reducao = await AlterarCupomSemCommitAsync(cupomId, "limite_usos = 1"))
        {
            confirmacao = ConfirmarAsync(c.NegocioId, a, "71966664444", "reduz@teste.com", "REDUZ");
            (await EsperarBloqueadosAsync(1, confirmacao)).Should().BeTrue();
            await reducao.ConfirmarAsync();
        }

        (await confirmacao).Sucesso.Should().BeTrue();
        (await EstadoAsync(a)).DescontoAplicado.Should().Be(0);
        (await CupomAsync(cupomId)).Should().Match<Cupom>(x => x.UsosAtuais == 1 && x.LimiteUsos == 1);
    }

    public static TheoryData<string> AlteracoesDoPainel => new() { "desativar", "reduzir_limite" };

    [Theory]
    [MemberData(nameof(AlteracoesDoPainel))]
    public async Task Alteracao_pelo_painel_espera_a_confirmacao_em_andamento_e_nao_desfaz_o_uso(string alteracao)
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "PAINEL", limite: 5, usosIniciais: 1);
        var a = await SemearReservaAsync(c, 13);
        var clienteId = await CriarClienteSemEmailAsync(c.NegocioId, "71966665555");

        // A confirmação trava o cupom e para no UPDATE do cliente (preenche o e-mail vazio), que a conexão externa segura.
        Task<ResultadoAgendamento> confirmacao;
        Task<bool> painel;
        await using (var trava = await TravarLinhaAsync("clientes", clienteId))
        {
            confirmacao = ConfirmarAsync(c.NegocioId, a, "71966665555", "painel@teste.com", "PAINEL");
            (await EsperarBloqueadosAsync(1, confirmacao)).Should().BeTrue();

            painel = AlterarPeloPainelAsync(c.NegocioId, cupomId, alteracao);
            (await EsperarBloqueadosAsync(2, confirmacao, painel)).Should().BeTrue("o painel precisa esperar a linha do cupom");
        }

        (await confirmacao).Sucesso.Should().BeTrue();
        (await painel).Should().BeTrue();
        (await EstadoAsync(a)).DescontoAplicado.Should().Be(Desconto, "a confirmação chegou primeiro");
        var cupom = await CupomAsync(cupomId);
        cupom.UsosAtuais.Should().Be(2, "o painel grava depois e não pode voltar o contador");
        if (alteracao == "desativar")
            cupom.Ativo.Should().BeFalse();
        else
            cupom.LimiteUsos.Should().Be(2);
    }

    // ---------------------------------------------------------------- rollback e nova tentativa

    [Fact]
    public async Task Falha_ao_gravar_a_confirmacao_desfaz_o_uso_junto_e_nao_avisa_ninguem()
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "FALHA", limite: 1);
        var a = await SemearReservaAsync(c, 13);

        ResultadoAgendamento resultado;
        await using (await InjetarFalhaAsync("agendamentos", "23P01", vezes: null))
            resultado = await ConfirmarAsync(c.NegocioId, a, "71966666666", "falha@teste.com", "FALHA");

        resultado.Sucesso.Should().BeFalse();
        resultado.Conflito.Should().BeTrue();
        var estado = await EstadoAsync(a);
        (estado.Status, estado.CupomId, estado.DescontoAplicado).Should().Be((StatusAgendamento.Reservado, (Guid?)null, 0m));
        (await UsosAsync(cupomId)).Should().Be(0);
        Avisos("falha@teste.com").Should().Be(0);

        // O uso que não foi gravado continua disponível.
        var b = await SemearReservaAsync(c, 15);
        (await ConfirmarAsync(c.NegocioId, b, "71966666667", "falha.b@teste.com", "FALHA")).Sucesso.Should().BeTrue();
        (await EstadoAsync(b)).DescontoAplicado.Should().Be(Desconto);
        (await UsosAsync(cupomId)).Should().Be(1);
    }

    [Fact]
    public async Task Nova_tentativa_da_execution_strategy_rele_o_cupom_e_conta_o_uso_uma_vez()
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "TENTA", limite: 1);
        var a = await SemearReservaAsync(c, 13);
        var b = await SemearReservaAsync(c, 14);

        // A primeira gravação do cupom falha com um erro transitório (40001); a estratégia repete a transação inteira.
        ResultadoAgendamento resultado;
        await using (await InjetarFalhaAsync("cupons", "40001", vezes: 1))
            resultado = await ConfirmarAsync(c.NegocioId, a, "71966667777", "tenta@teste.com", "TENTA");

        resultado.Sucesso.Should().BeTrue();
        (await EstadoAsync(a)).DescontoAplicado.Should().Be(Desconto);
        (await UsosAsync(cupomId)).Should().Be(1);
        Avisos("tenta@teste.com").Should().Be(1);
        (await _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().CountAsync(x => x.NegocioId == c.NegocioId && x.Email == "tenta@teste.com")))
            .Should().Be(1, "a primeira tentativa não pode deixar cliente duplicado");

        (await ConfirmarAsync(c.NegocioId, b, "71966667778", "tenta.b@teste.com", "TENTA")).Sucesso.Should().BeTrue();
        (await EstadoAsync(b)).DescontoAplicado.Should().Be(0, "o único uso já foi contado");
        (await UsosAsync(cupomId)).Should().Be(1);
    }

    // ---------------------------------------------------------------- deadlock e isolamento

    [Fact]
    public async Task Confirmacoes_e_painel_misturados_terminam_sem_deadlock_e_com_contador_coerente()
    {
        var c = await ArranjarAsync();
        var cupomId = await CriarCupomAsync(c.NegocioId, "MISTO", limite: 4);
        var reservas = await SemearReservasAsync(c, 8);

        using var largada = new ManualResetEventSlim(false);
        var confirmacoes = reservas.Select((id, i) => Task.Run(async () =>
        {
            largada.Wait();
            return await ConfirmarAsync(c.NegocioId, id, $"7196668{i:D4}", $"misto{i}@teste.com", "MISTO");
        })).ToList();
        var painel = Enumerable.Range(0, 4).Select(i => Task.Run(async () =>
        {
            largada.Wait();
            return await AlterarPeloPainelAsync(c.NegocioId, cupomId, i % 2 == 0 ? "ativar" : "manter_limite");
        })).ToList();
        largada.Set();

        var todas = Task.WhenAll(Task.WhenAll(confirmacoes), Task.WhenAll(painel));
        (await Task.WhenAny(todas, Task.Delay(TimeSpan.FromSeconds(60)))).Should().Be(todas, "nenhuma combinação pode travar");
        await todas;

        confirmacoes.Should().OnlyContain(t => t.Result.Sucesso);
        painel.Should().OnlyContain(t => t.Result);
        var comDesconto = (await EstadosAsync(reservas)).Count(e => e.DescontoAplicado == Desconto);
        comDesconto.Should().Be(4);
        (await UsosAsync(cupomId)).Should().Be(4);
    }

    [Fact]
    public async Task Cupom_de_outro_negocio_com_o_mesmo_codigo_nao_e_usado_nem_travado()
    {
        var c = await ArranjarAsync();
        var outro = await ArranjarAsync();
        var doOutro = await CriarCupomAsync(outro.NegocioId, "IGUAL", limite: 1);
        var deste = await CriarCupomAsync(c.NegocioId, "IGUAL", limite: 1, usosIniciais: 1);
        var a = await SemearReservaAsync(c, 13);

        // A linha do cupom do outro negócio fica travada o tempo todo: a confirmação daqui não pode depender dela.
        await using (var trava = await TravarLinhaAsync("cupons", doOutro))
        {
            var resultado = await ConfirmarAsync(c.NegocioId, a, "71966669999", "igual@teste.com", "IGUAL");
            resultado.Sucesso.Should().BeTrue();
        }

        (await EstadoAsync(a)).DescontoAplicado.Should().Be(0, "o cupom deste negócio está esgotado");
        (await UsosAsync(deste)).Should().Be(1);
        (await UsosAsync(doOutro)).Should().Be(0);
        (await AlterarPeloPainelAsync(c.NegocioId, doOutro, "desativar")).Should().BeFalse("o painel só alcança cupons do próprio negócio");
        (await CupomAsync(doOutro)).Ativo.Should().BeTrue();
    }

    // ---------------------------------------------------------------- apoio

    private sealed record Cenario(Guid NegocioId, SemeadorDeAgenda.CenarioDeAgenda Agenda, DateOnly Segunda);

    private async Task<Cenario> ArranjarAsync()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var agenda = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);
        return new Cenario(negocioId, agenda, segunda);
    }

    /// <summary>Reserva sem cliente, de R$ 50 e 30 min, num horário fixo da segunda (não sobrepõe as outras).</summary>
    private Task<Guid> SemearReservaAsync(Cenario c, int hora, int minuto = 0) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var itens = new[] { new ItemServicoAgendamento(c.Agenda.ServicoId, "Serviço de Teste", Preco, 30) };
            var reserva = Agendamento.CriarReserva(c.NegocioId, c.Agenda.ProfissionalId, null,
                SemeadorDeComissoes.Local(c.Segunda, hora, minuto), itens, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));
            db.Agendamentos.Add(reserva);
            await db.SaveChangesAsync();
            return reserva.Id;
        });

    private async Task<List<Guid>> SemearReservasAsync(Cenario c, int quantidade)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < quantidade; i++)
            ids.Add(await SemearReservaAsync(c, 8 + i / 2, i % 2 * 30));
        return ids;
    }

    private Task<Guid> CriarCupomAsync(Guid negocioId, string codigo, int? limite, int usosIniciais = 0) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var cupom = Cupom.Criar(negocioId, codigo, TipoDescontoCupom.Percentual, 10m, limiteUsos: limite);
            for (var i = 0; i < usosIniciais; i++)
                cupom.RegistrarUso();
            db.Cupons.Add(cupom);
            await db.SaveChangesAsync();
            return cupom.Id;
        });

    private Task<Guid> CriarClienteSemEmailAsync(Guid negocioId, string telefone) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var cliente = Cliente.Criar(negocioId, "Cliente B5", TelefoneE164.Criar(telefone), OrigemCliente.LinkPublico);
            db.Clientes.Add(cliente);
            await db.SaveChangesAsync();
            return cliente.Id;
        });

    /// <summary>Cada confirmação num escopo (e conexão) próprio, como requisições diferentes.</summary>
    private async Task<ResultadoAgendamento> ConfirmarAsync(Guid negocioId, Guid reservaId, string telefone, string email, string cupom)
    {
        await Task.Yield();
        using var escopo = Escopo(negocioId);
        return await escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>().ConfirmarReservaPublicaAsync(
            new ConfirmarReservaPublica(reservaId, "Cliente B5", telefone, email, null, cupom, "127.0.0.1"));
    }

    private async Task<ResultadoAgendamento[]> ConfirmarTodasJuntasAsync(Cenario c, List<Guid> reservas, string cupom, string prefixo)
    {
        using var largada = new ManualResetEventSlim(false);
        var tarefas = reservas.Select((id, i) => Task.Run(async () =>
        {
            largada.Wait();
            return await ConfirmarAsync(c.NegocioId, id, $"719667{prefixo.Length}{i:D4}", $"{prefixo}{i}@teste.com", cupom);
        })).ToList();
        largada.Set();
        return await Task.WhenAll(tarefas);
    }

    private async Task<bool> AlterarPeloPainelAsync(Guid negocioId, Guid cupomId, string alteracao)
    {
        await Task.Yield();
        using var escopo = Escopo(negocioId);
        var gerenciador = escopo.ServiceProvider.GetRequiredService<IGerenciadorCupons>();
        return alteracao switch
        {
            "desativar" => await gerenciador.DesativarAsync(cupomId),
            "ativar" => await gerenciador.AtivarAsync(cupomId),
            "reduzir_limite" => await gerenciador.AtualizarAsync(cupomId, new AtualizarCupom(null, 2, null)),
            "manter_limite" => await gerenciador.AtualizarAsync(cupomId, new AtualizarCupom(null, 4, null)),
            _ => throw new ArgumentOutOfRangeException(nameof(alteracao)),
        };
    }

    private IServiceScope Escopo(Guid negocioId)
    {
        var escopo = _fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioId);
        return escopo;
    }

    private Task<int> UsosAsync(Guid cupomId) => _fabrica.NoBancoAsync(async db => (await CupomAsync(db, cupomId)).UsosAtuais);

    private Task<Cupom> CupomAsync(Guid cupomId) => _fabrica.NoBancoAsync(db => CupomAsync(db, cupomId));

    private static Task<Cupom> CupomAsync(Plataforma.Infraestrutura.Persistencia.PlataformaDbContext db, Guid cupomId) =>
        db.Cupons.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == cupomId);

    private Task<Agendamento> EstadoAsync(Guid id) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking().Include(a => a.Servicos).SingleAsync(a => a.Id == id));

    private async Task<List<Agendamento>> EstadosAsync(IEnumerable<Guid> ids)
    {
        var estados = new List<Agendamento>();
        foreach (var id in ids)
            estados.Add(await EstadoAsync(id));
        return estados;
    }

    private int Avisos(string email) => _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Count(e => e.Destinatario == email);

    /// <summary>Segura a linha (<c>FOR UPDATE</c>) numa conexão externa até o descarte, sem alterar nada.</summary>
    private async Task<TransacaoExterna> TravarLinhaAsync(string tabela, Guid id)
    {
        var externa = await TransacaoExterna.AbrirAsync(_postgres.ConnectionString);
        await externa.ExecutarAsync("SELECT 1 FROM " + tabela + " WHERE id = @id FOR UPDATE", id);
        return externa;
    }

    /// <summary>Uma alteração do cupom ainda não confirmada, que segura a linha até <see cref="TransacaoExterna.ConfirmarAsync"/>.</summary>
    private async Task<TransacaoExterna> AlterarCupomSemCommitAsync(Guid cupomId, string atribuicao)
    {
        var externa = await TransacaoExterna.AbrirAsync(_postgres.ConnectionString);
        await externa.ExecutarAsync("UPDATE cupons SET " + atribuicao + " WHERE id = @id", cupomId);
        return externa;
    }

    /// <summary>Sinal vindo do próprio banco: pelo menos <paramref name="quantos"/> transações esperando uma trava.</summary>
    private async Task<bool> EsperarBloqueadosAsync(int quantos, params Task[] concorrentes)
    {
        await using var conexao = new NpgsqlConnection(_postgres.ConnectionString);
        await conexao.OpenAsync();
        for (var i = 0; i < 4000 && !concorrentes.Any(t => t.IsCompleted); i++)
        {
            await using var consulta = new NpgsqlCommand("SELECT count(DISTINCT pid) FROM pg_locks WHERE NOT granted", conexao);
            if ((long)(await consulta.ExecuteScalarAsync())! >= quantos)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gatilho só no banco de teste: o UPDATE na tabela falha com o SQLSTATE dado — sempre, ou só nas primeiras
    /// <paramref name="vezes"/> (contadas por uma sequência, que não volta no rollback). DDL não aceita parâmetro no nome
    /// do objeto, então o SQL é montado aqui só com valores gerados pelo próprio teste.
    /// </summary>
    private async Task<IAsyncDisposable> InjetarFalhaAsync(string tabela, string sqlState, int? vezes)
    {
        var nome = "b51_falha_" + Guid.NewGuid().ToString("N")[..8];
        var condicao = vezes is { } n ? "nextval('" + nome + "') <= " + n : "true";
        var comandos = new[]
        {
            "CREATE SEQUENCE " + nome,
            "CREATE FUNCTION " + nome + "() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF " + condicao
                + " THEN RAISE EXCEPTION 'simulada' USING ERRCODE = '" + sqlState + "'; END IF; RETURN NEW; END $$",
            "CREATE TRIGGER " + nome + " BEFORE UPDATE ON " + tabela + " FOR EACH ROW EXECUTE FUNCTION " + nome + "()",
        };
        var remocao = new[]
        {
            "DROP TRIGGER IF EXISTS " + nome + " ON " + tabela,
            "DROP FUNCTION IF EXISTS " + nome + "()",
            "DROP SEQUENCE IF EXISTS " + nome,
        };

        await ExecutarDdlAsync(comandos);
        return new Remocao(() => ExecutarDdlAsync(remocao));
    }

    private Task ExecutarDdlAsync(IEnumerable<string> comandos) =>
        _fabrica.NoBancoAsync(async db =>
        {
            foreach (var comando in comandos)
                await db.Database.ExecuteSqlRawAsync(comando);
            return 0;
        });

    private sealed class Remocao(Func<Task> remover) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await remover();
    }

    private sealed class TransacaoExterna : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conexao;
        private readonly NpgsqlTransaction _transacao;
        private bool _encerrada;

        private TransacaoExterna(NpgsqlConnection conexao, NpgsqlTransaction transacao)
        {
            _conexao = conexao;
            _transacao = transacao;
        }

        public static async Task<TransacaoExterna> AbrirAsync(string connectionString)
        {
            var conexao = new NpgsqlConnection(connectionString);
            await conexao.OpenAsync();
            return new TransacaoExterna(conexao, await conexao.BeginTransactionAsync());
        }

        public async Task ExecutarAsync(string sql, Guid id)
        {
            await using var comando = new NpgsqlCommand(sql, _conexao, _transacao);
            comando.Parameters.AddWithValue("id", id);
            await comando.ExecuteNonQueryAsync();
        }

        public async Task ConfirmarAsync()
        {
            await _transacao.CommitAsync();
            _encerrada = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_encerrada)
                await _transacao.RollbackAsync();
            await _transacao.DisposeAsync();
            await _conexao.DisposeAsync();
        }
    }
}
