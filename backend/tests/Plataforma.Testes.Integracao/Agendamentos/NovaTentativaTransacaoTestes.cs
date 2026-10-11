using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Aplicacao.Cupons;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Cupons;
using Plataforma.Dominio.Fidelidade;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>
/// Nova tentativa da <c>ExecutionStrategy</c> (B6/B6.1). Cada teste roda duas vezes com as mesmas expectativas: sem falha e com
/// uma falha transitória (40001) na primeira tentativa — num INSERT, num UPDATE ou no COMMIT (gatilho de constraint deferido).
/// A falha só acontece uma vez (contada por uma sequência, que o rollback não desfaz), e o teste confere que a estratégia de fato
/// repetiu. O resultado com nova tentativa tem de ser idêntico ao normal: mesma resposta, mesmas linhas, auditoria, estoque,
/// comissão e avisos, lidos num contexto novo.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class NovaTentativaTransacaoTestes : IAsyncLifetime
{
    private const string EmailProf = "prof.retry@teste.com";
    private const string EmailDestino = "destino.retry@teste.com";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public NovaTentativaTransacaoTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    public static TheoryData<bool> SemEComFalha => new() { false, true };

    // ---------------------------------------------------------------- agenda: criação

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Criacao_comum_pelo_painel(bool falha)
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 10);

        HttpResponseMessage resposta;
        await using (var f = await FalharAsync(falha, "agendamentos", "INSERT"))
        {
            resposta = await c.Admin.PostAsJsonAsync("/painel/agendamentos",
                new CriarAgendamento(c.Agenda.ProfissionalId, c.Agenda.ClienteId, [c.Agenda.ServicoId], inicio));
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        (await AgendamentosEmAsync(c, inicio)).Should().ContainSingle().Which.NegocioId.Should().Be(c.NegocioId);
        Avisos(EmailProf).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Criacao_forcada(bool falha)
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 19);

        HttpResponseMessage resposta;
        await using (var f = await FalharAsync(falha, "agendamentos", "INSERT"))
        {
            resposta = await c.Admin.PostAsJsonAsync("/painel/agendamentos/forcados", new
            {
                profissionalId = c.Agenda.ProfissionalId, clienteId = c.Agenda.ClienteId, servicoIds = new[] { c.Agenda.ServicoId },
                inicio, observacoes = (string?)null, motivo = "Cliente especial",
            });
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        (await AgendamentosEmAsync(c, inicio)).Should().ContainSingle().Which.Forcado.Should().BeTrue();
        (await AuditoriaAsync(c, AcoesAuditoria.ForcarAgendamento)).Should().Be(1);
        Avisos(EmailProf).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Reserva_publica(bool falha)
    {
        var c = await ArranjarAsync();
        var inicio = Local(c, 11);

        ResultadoAgendamento resultado;
        await using (var f = await FalharAsync(falha, "agendamentos", "INSERT"))
        {
            using var escopo = Escopo(c.NegocioId);
            resultado = await Servico<IServicoAgendamentos>(escopo)
                .CriarReservaPublicaAsync(new CriarReservaPublica(c.Agenda.ProfissionalId, [c.Agenda.ServicoId], inicio));
            await f.ConferirAsync();
        }

        resultado.Sucesso.Should().BeTrue();
        (await AgendamentosEmAsync(c, inicio)).Should().ContainSingle().Which.Status.Should().Be(StatusAgendamento.Reservado);
    }

    // ---------------------------------------------------------------- agenda: mudanças

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Remarcacao(bool falha)
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);
        var novo = Local(c, 14);

        HttpResponseMessage resposta;
        await using (var f = await FalharAsync(falha, "agendamentos", "UPDATE"))
        {
            resposta = await c.Admin.PutAsJsonAsync($"/painel/agendamentos/{id}/mover", new { novoInicio = novo });
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var estado = await EstadoAsync(id);
        (estado.Inicio, estado.Fim).Should().Be((novo, novo.AddMinutes(20)));
        Avisos(EmailProf).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Transferencia(bool falha)
    {
        var c = await ArranjarAsync();
        var destino = await CriarDestinoAsync(c);
        var id = await SemearAsync(c, 10);

        HttpResponseMessage resposta;
        await using (var f = await FalharAsync(falha, "agendamentos", "UPDATE"))
        {
            resposta = await c.Admin.PostAsJsonAsync(
                $"/painel/profissionais/{c.Agenda.ProfissionalId}/agendamentos-futuros/{id}/transferir", new { novoProfissionalId = destino });
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await EstadoAsync(id)).ProfissionalId.Should().Be(destino);
        (await AuditoriaAsync(c, AcoesAuditoria.TransferirAgendamento)).Should().Be(1);
        Avisos(EmailDestino).Should().Be(1);
    }

    public static TheoryData<string, bool, StatusAgendamento> Transicoes => new()
    {
        { "iniciar", false, StatusAgendamento.EmAtendimento }, { "iniciar", true, StatusAgendamento.EmAtendimento },
        { "faltou", false, StatusAgendamento.Faltou }, { "faltou", true, StatusAgendamento.Faltou },
        { "cancelar", false, StatusAgendamento.Cancelado }, { "cancelar", true, StatusAgendamento.Cancelado },
    };

    [Theory]
    [MemberData(nameof(Transicoes))]
    public async Task Inicio_falta_e_cancelamento(string acao, bool falha, StatusAgendamento esperado)
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        HttpResponseMessage resposta;
        await using (var f = await FalharAsync(falha, "agendamentos", "UPDATE"))
        {
            resposta = await c.Admin.PostAsync($"/painel/agendamentos/{id}/{acao}", null);
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await EstadoAsync(id)).Status.Should().Be(esperado);
        Avisos(EmailProf).Should().Be(acao == "cancelar" ? 1 : 0);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Cancelamento_avisando_o_cliente_grava_uma_auditoria(bool falha)
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);

        bool cancelado;
        await using (var f = await FalharAsync(falha, "agendamentos", "UPDATE"))
        {
            using var escopo = Escopo(c.NegocioId);
            cancelado = await Servico<IServicoAgendamentos>(escopo).CancelarAvisandoClienteAsync(id);
            await f.ConferirAsync();
        }

        cancelado.Should().BeTrue();
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Cancelado);
        (await AuditoriaAsync(c, AcoesAuditoria.CancelarAgendamento)).Should().Be(1);
        Avisos(EmailProf).Should().Be(1);
    }

    // ---------------------------------------------------------------- conclusão e reabertura

    public static TheoryData<string> PontosDaConclusao => new() { "sem_falha", "update_agendamento", "commit", "insert_selo" };

    [Theory]
    [MemberData(nameof(PontosDaConclusao))]
    public async Task Conclusao_grava_comissao_e_um_selo(string ponto)
    {
        var c = await ArranjarAsync(comissao: 40m, fidelidade: true);
        var id = await SemearAsync(c, 10);

        HttpResponseMessage resposta;
        await using (var f = ponto switch
        {
            "update_agendamento" => await FalharAsync(true, "agendamentos", "UPDATE"),
            "commit" => await FalharNoCommitAsync("agendamentos", "UPDATE"),
            "insert_selo" => await FalharAsync(true, "selos_cliente", "INSERT"),
            _ => await FalharAsync(false, "agendamentos", "UPDATE"),
        })
        {
            resposta = await c.Admin.PostAsync($"/painel/agendamentos/{id}/concluir", null);
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var estado = await EstadoAsync(id);
        estado.Status.Should().Be(StatusAgendamento.Concluido);
        estado.Servicos.Should().ContainSingle().Which.ComissaoValor.Should().Be(20m, "40% de R$ 50, mesmo depois da nova tentativa");
        (await SelosAsync(id)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Reabertura(bool falha)
    {
        var c = await ArranjarAsync(comissao: 40m, fidelidade: true);
        var id = await SemearAsync(c, 10);
        (await c.Admin.PostAsync($"/painel/agendamentos/{id}/concluir", null)).EnsureSuccessStatusCode();

        HttpResponseMessage resposta;
        await using (var f = await FalharAsync(falha, "agendamentos", "UPDATE"))
        {
            resposta = await c.Admin.PostAsync($"/painel/agendamentos/{id}/reabrir", null);
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var estado = await EstadoAsync(id);
        estado.Status.Should().Be(StatusAgendamento.Agendado);
        estado.Servicos.Should().ContainSingle().Which.ComissaoValor.Should().BeNull();
        (await SelosAsync(id)).Should().Be(0);
        (await AuditoriaAsync(c, AcoesAuditoria.ReabrirAtendimento)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Ajuste_de_valor(bool falha)
    {
        var c = await ArranjarAsync();
        var id = await SemearAsync(c, 10);
        (await c.Admin.PostAsync($"/painel/agendamentos/{id}/iniciar", null)).EnsureSuccessStatusCode();
        var linha = (await EstadoAsync(id)).Servicos.Single().Id;

        HttpResponseMessage resposta;
        await using (var f = await FalharAsync(falha, "ajustes_valor_atendimento", "INSERT"))
        {
            resposta = await c.Admin.PostAsJsonAsync($"/painel/agendamentos/{id}/servicos/{linha}/ajuste",
                new AjustarValor("Desconto", "Reais", 10m, "Cortesia"));
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EstadoAsync(id)).Servicos.Single().ValorCobrado.Should().Be(40m);
        var ajustes = await _fabrica.NoBancoAsync(db => db.AjustesValorAtendimento.IgnoreQueryFilters()
            .Where(a => a.AgendamentoId == id).Select(a => new { a.ValorAntes, a.ValorDepois }).ToListAsync());
        ajustes.Should().ContainSingle().Which.Should().Be(new { ValorAntes = 50m, ValorDepois = 40m });
        (await AuditoriaAsync(c, AcoesAuditoria.AjustarValorAtendimento)).Should().Be(1);
    }

    // ---------------------------------------------------------------- estoque e vendas

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Entrada_de_estoque(bool falha)
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        HttpResponseMessage resposta;
        await using (var f = await FalharAsync(falha, "movimentos_estoque", "INSERT"))
        {
            resposta = await c.Admin.PostAsJsonAsync($"/painel/estoque/produtos/{produto}/entradas", new RegistrarEntrada(5, 3m));
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EstoqueAsync(produto)).Should().Be((15, 15, 2));
        (await AuditoriaAsync(c, AcoesAuditoria.EntradaEstoque)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Consumo_interno(bool falha)
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        Guid? consumo;
        await using (var f = await FalharAsync(falha, "lancamentos_saldo_devedor", "INSERT"))
        {
            using var escopo = Escopo(c.NegocioId);
            consumo = await Servico<IServicoSaldoDevedor>(escopo).LancarConsumoAsync(produto, new LancarConsumo(c.Agenda.ProfissionalId, 2));
            await f.ConferirAsync();
        }

        consumo.Should().NotBeNull();
        (await EstoqueAsync(produto)).Should().Be((8, 8, 2));
        (await LancamentosAsync(c)).Should().ContainSingle().Which.Should().Be(10m, "2 × custo de R$ 5");
        (await AuditoriaAsync(c, AcoesAuditoria.LancarConsumoInterno)).Should().Be(1);
    }

    public static TheoryData<string> PontosDaVenda => new() { "sem_falha", "insert", "commit" };

    [Theory]
    [MemberData(nameof(PontosDaVenda))]
    public async Task Venda(string ponto)
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);

        HttpResponseMessage resposta;
        await using (var f = ponto switch
        {
            "insert" => await FalharAsync(true, "vendas_produto", "INSERT"),
            "commit" => await FalharNoCommitAsync("vendas_produto", "INSERT"),
            _ => await FalharAsync(false, "vendas_produto", "INSERT"),
        })
        {
            resposta = await c.Admin.PostAsJsonAsync("/painel/vendas", new LancarVenda([new ItemLancarVenda(produto, 2, 20m)], c.Agenda.ProfissionalId, null));
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        (await VendasAsync(c)).Should().ContainSingle().Which.Total.Should().Be(40m);
        (await EstoqueAsync(produto)).Should().Be((8, 8, 2));
        (await AuditoriaAsync(c, AcoesAuditoria.VendaProduto)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Estorno_de_venda(bool falha)
    {
        var c = await ArranjarAsync();
        var produto = await CriarProdutoAsync(c, 10);
        (await c.Admin.PostAsJsonAsync("/painel/vendas", new LancarVenda([new ItemLancarVenda(produto, 2, 20m)], c.Agenda.ProfissionalId, null)))
            .EnsureSuccessStatusCode();
        var venda = (await VendasAsync(c)).Single().Id;

        bool estornada;
        await using (var f = await FalharAsync(falha, "vendas_produto", "UPDATE"))
        {
            using var escopo = Escopo(c.NegocioId);
            estornada = await Servico<IServicoVendas>(escopo).EstornarAsync(venda, "Devolução");
            await f.ConferirAsync();
        }

        estornada.Should().BeTrue();
        (await VendasAsync(c)).Should().ContainSingle().Which.EstornadaEm.Should().NotBeNull();
        (await EstoqueAsync(produto)).Should().Be((10, 10, 3));
        (await AuditoriaAsync(c, AcoesAuditoria.EstornarVenda)).Should().Be(1);
    }

    // ---------------------------------------------------------------- saldo devedor e quinzenas

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Edicao_de_vale(bool falha)
    {
        var c = await ArranjarAsync();
        var vale = await LancarValeAsync(c, 30m);

        ResultadoLancamentoSaldo resultado;
        await using (var f = await FalharAsync(falha, "lancamentos_saldo_devedor", "UPDATE"))
        {
            using var escopo = Escopo(c.NegocioId);
            resultado = await Servico<IServicoSaldoDevedor>(escopo).AlterarAsync(vale, new AlterarLancamentoSaldo(Valor: 45m), _ => true);
            await f.ConferirAsync();
        }

        resultado.Sucesso.Should().BeTrue();
        (await LancamentosAsync(c)).Should().ContainSingle().Which.Should().Be(45m);
        (await AuditoriaAsync(c, AcoesAuditoria.EditarLancamentoSaldo)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Fechamento_de_quinzena_desconta_o_vale_uma_vez(bool falha)
    {
        var c = await ArranjarAsync(comissao: 40m, acertoPorQuinzena: true);
        var periodo = await QuinzenaComAtendimentoEValeAsync(c);

        ResultadoQuinzena resultado;
        await using (var f = await FalharAsync(falha, "fechamentos_comissao", "INSERT"))
        {
            using var escopo = Escopo(c.NegocioId);
            resultado = await Servico<IServicoQuinzenas>(escopo).FecharAsync(periodo, confirmarPendentes: true);
            await f.ConferirAsync();
        }

        resultado.Sucesso.Should().BeTrue();
        var (fechamentos, quitacoes, estado) = await QuinzenaAsync(c, periodo);
        fechamentos.Should().ContainSingle().Which.Should().Be((20m, 10m, 10m), "comissão 20, vale 10 descontado, líquido 10");
        quitacoes.Should().Be(1);
        estado.Should().Be("Fechada");
        (await AuditoriaAsync(c, AcoesAuditoria.FecharQuinzena)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Exclusao_de_vale_ja_descontado_estorna_o_desconto_uma_vez(bool falha)
    {
        var c = await ArranjarAsync(comissao: 40m, acertoPorQuinzena: true);
        var periodo = await QuinzenaComAtendimentoEValeAsync(c);
        using (var escopo = Escopo(c.NegocioId))
            (await Servico<IServicoQuinzenas>(escopo).FecharAsync(periodo, confirmarPendentes: true)).Sucesso.Should().BeTrue();
        var vale = await _fabrica.NoBancoAsync(db => db.LancamentosSaldoDevedor.IgnoreQueryFilters().Where(l => l.NegocioId == c.NegocioId).Select(l => l.Id).SingleAsync());

        ResultadoLancamentoSaldo resultado;
        await using (var f = await FalharAsync(falha, "fechamentos_comissao", "UPDATE"))
        {
            using var escopo = Escopo(c.NegocioId);
            resultado = await Servico<IServicoSaldoDevedor>(escopo).ExcluirAsync(vale, confirmarQuitado: true, _ => true);
            await f.ConferirAsync();
        }

        resultado.Sucesso.Should().BeTrue();
        (await LancamentosAsync(c)).Should().BeEmpty();
        var (fechamentos, quitacoes, _) = await QuinzenaAsync(c, periodo);
        fechamentos.Should().ContainSingle().Which.Should().Be((20m, 0m, 20m), "o desconto volta ao líquido uma vez só");
        quitacoes.Should().Be(0);
        (await AuditoriaAsync(c, AcoesAuditoria.ExcluirLancamentoSaldo)).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Reabertura_de_quinzena(bool falha)
    {
        var c = await ArranjarAsync(comissao: 40m, acertoPorQuinzena: true);
        var periodo = await QuinzenaComAtendimentoEValeAsync(c);
        using (var escopo = Escopo(c.NegocioId))
            (await Servico<IServicoQuinzenas>(escopo).FecharAsync(periodo, confirmarPendentes: true)).Sucesso.Should().BeTrue();

        ResultadoQuinzena resultado;
        await using (var f = await FalharAsync(falha, "periodos_comissao", "UPDATE"))
        {
            using var escopo = Escopo(c.NegocioId);
            resultado = await Servico<IServicoQuinzenas>(escopo).ReabrirAsync(periodo, "Conferência");
            await f.ConferirAsync();
        }

        resultado.Sucesso.Should().BeTrue();
        var (fechamentos, quitacoes, estado) = await QuinzenaAsync(c, periodo);
        fechamentos.Should().BeEmpty();
        quitacoes.Should().Be(0);
        estado.Should().Be("Aberta");
        (await AuditoriaAsync(c, AcoesAuditoria.ReabrirQuinzena)).Should().Be(1);
    }

    // ---------------------------------------------------------------- cadastros com transação

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Limite_do_plano_ao_criar_vinculado_e_ao_reativar(bool falha)
    {
        var c = await ArranjarAsync();
        var usuario = await _fabrica.NoBancoAsync(async db =>
        {
            var u = Usuario.Criar(c.NegocioId, "Pessoa Retry", $"retry{Guid.NewGuid():N}@teste.com", Perfil.Profissional, "hash");
            db.Usuarios.Add(u);
            await db.SaveChangesAsync();
            return u.Id;
        });

        Guid criado;
        await using (var f = await FalharAsync(falha, "profissionais", "INSERT"))
        {
            using var escopo = Escopo(c.NegocioId);
            criado = await Servico<IGerenciadorProfissionais>(escopo).CriarAsync(new CriarProfissional("Pessoa Retry", UsuarioId: usuario));
            await f.ConferirAsync();
        }

        (await _fabrica.NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().CountAsync(p => p.NegocioId == c.NegocioId && p.Nome == "Pessoa Retry")))
            .Should().Be(1);
        (await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().Where(u => u.Id == usuario).Select(u => u.ProfissionalId).SingleAsync()))
            .Should().Be(criado, "o vínculo com o usuário que já existia precisa sobreviver à nova tentativa");

        await _fabrica.NoBancoAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET ativo = false WHERE id = {criado}"));
        await using (var f = await FalharAsync(falha, "profissionais", "UPDATE"))
        {
            using var escopo = Escopo(c.NegocioId);
            (await Servico<IGerenciadorProfissionais>(escopo).AtivarAsync(criado)).Should().BeTrue();
            await f.ConferirAsync();
        }

        (await _fabrica.NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().Where(p => p.Id == criado).Select(p => p.Ativo).SingleAsync()))
            .Should().BeTrue("a reativação feita na nova tentativa precisa ser gravada");
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Cupom_alterado_pelo_painel(bool falha)
    {
        var c = await ArranjarAsync();
        var cupom = await _fabrica.NoBancoAsync(async db =>
        {
            var x = Cupom.Criar(c.NegocioId, "RETRY", TipoDescontoCupom.Percentual, 10m, limiteUsos: 3);
            db.Cupons.Add(x);
            await db.SaveChangesAsync();
            return x.Id;
        });

        await using (var f = await FalharAsync(falha, "cupons", "UPDATE"))
        {
            using var escopo = Escopo(c.NegocioId);
            (await Servico<IGerenciadorCupons>(escopo).DesativarAsync(cupom)).Should().BeTrue();
            await f.ConferirAsync();
        }

        (await _fabrica.NoBancoAsync(db => db.Cupons.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == cupom)))
            .Should().Match<Cupom>(x => !x.Ativo && x.UsosAtuais == 0 && x.LimiteUsos == 3);
    }

    // ---------------------------------------------------------------- confirmações

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Confirmacao_publica_com_falha_no_commit(bool falha)
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, clienteId: null);
        await _fabrica.NoBancoAsync(async db =>
        {
            db.Cupons.Add(Cupom.Criar(c.NegocioId, "PUB", TipoDescontoCupom.Percentual, 10m, limiteUsos: 1));
            return await db.SaveChangesAsync();
        });

        ResultadoAgendamento resultado;
        await using (var f = falha ? await FalharNoCommitAsync("agendamentos", "UPDATE") : await FalharAsync(false, "agendamentos", "UPDATE"))
        {
            using var escopo = Escopo(c.NegocioId);
            resultado = await Servico<IServicoAgendamentos>(escopo).ConfirmarReservaPublicaAsync(
                new ConfirmarReservaPublica(id, "Cliente Retry", "71955550000", "publico.retry@teste.com", "obs", "PUB", "127.0.0.1"));
            await f.ConferirAsync();
        }

        resultado.Sucesso.Should().BeTrue();
        var estado = await EstadoAsync(id);
        (estado.Status, estado.DescontoAplicado, estado.ConsentimentoData is not null, estado.Observacoes)
            .Should().Be((StatusAgendamento.Agendado, 5m, true, "obs"));
        (await _fabrica.NoBancoAsync(db => db.Cupons.IgnoreQueryFilters().Where(x => x.NegocioId == c.NegocioId).Select(x => x.UsosAtuais).SingleAsync()))
            .Should().Be(1);
        (await _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().CountAsync(x => x.NegocioId == c.NegocioId && x.Email == "publico.retry@teste.com")))
            .Should().Be(1);
        Avisos("publico.retry@teste.com").Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SemEComFalha))]
    public async Task Confirmacao_pelo_painel(bool falha)
    {
        var c = await ArranjarAsync();
        var id = await SemearReservaAsync(c, c.Agenda.ClienteId);

        HttpResponseMessage resposta;
        await using (var f = await FalharAsync(falha, "agendamentos", "UPDATE"))
        {
            resposta = await c.Admin.PostAsync($"/painel/agendamentos/{id}/confirmar", null);
            await f.ConferirAsync();
        }

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await EstadoAsync(id)).Status.Should().Be(StatusAgendamento.Agendado);
    }

    // ---------------------------------------------------------------- apoio

    private sealed record Cenario(Guid NegocioId, HttpClient Admin, SemeadorDeAgenda.CenarioDeAgenda Agenda, DateOnly Segunda);

    private async Task<Cenario> ArranjarAsync(decimal comissao = 0m, bool fidelidade = false, bool acertoPorQuinzena = false)
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador,
            Permissao.ForcarAgendamento, Permissao.AjustarValorAtendimento, Permissao.GerenciarEstoque, Permissao.VenderProdutos);
        var agenda = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        await _fabrica.NoBancoAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE profissionais SET email = {EmailProf}, percentual_comissao = {comissao}, acerto_por_quinzena = {acertoPorQuinzena} WHERE id = {agenda.ProfissionalId}");
            if (fidelidade)
                db.ProgramasFidelidade.Add(ProgramaFidelidade.Criar(negocioId, 5, "Um corte"));
            return await db.SaveChangesAsync();
        });

        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);

        _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Clear();
        return new Cenario(negocioId, admin, agenda, segunda);
    }

    private static DateTimeOffset Local(Cenario c, int hora) => SemeadorDeComissoes.Local(c.Segunda, hora, 0);

    private Task<Guid> SemearAsync(Cenario c, int hora) =>
        _fabrica.SemearAgendamentoAsync(c.NegocioId, c.Agenda.ProfissionalId, Local(c, hora), precos: [50m], servicoId: c.Agenda.ServicoId);

    private Task<Guid> SemearReservaAsync(Cenario c, Guid? clienteId) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var reserva = Agendamento.CriarReserva(c.NegocioId, c.Agenda.ProfissionalId, clienteId, Local(c, 10),
                [new ItemServicoAgendamento(c.Agenda.ServicoId, "Serviço de Teste", 50m, 30)], DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10));
            db.Agendamentos.Add(reserva);
            await db.SaveChangesAsync();
            return reserva.Id;
        });

    private async Task<Guid> CriarDestinoAsync(Cenario c)
    {
        var id = await _fabrica.CriarProfissionalAsync(c.NegocioId, "Destino Retry");
        await _fabrica.NoBancoAsync(async db =>
        {
            foreach (var dia in Enum.GetValues<DiaSemana>())
                db.HorariosTrabalho.Add(HorarioTrabalho.Criar(c.NegocioId, id, dia, new TimeOnly(8, 0), new TimeOnly(18, 0)));
            db.ProfissionalServicos.Add(ProfissionalServico.Criar(c.NegocioId, id, c.Agenda.ServicoId));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profissionais SET email = {EmailDestino} WHERE id = {id}");
            return await db.SaveChangesAsync();
        });
        return id;
    }

    private async Task<Guid> CriarProdutoAsync(Cenario c, int quantidade)
    {
        var resposta = await c.Admin.PostAsJsonAsync("/painel/estoque/produtos", new CriarProduto("Pomada Retry", null, 5m, 20m, quantidade));
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task<Guid> LancarValeAsync(Cenario c, decimal valor)
    {
        using var escopo = Escopo(c.NegocioId);
        return await Servico<IServicoSaldoDevedor>(escopo).LancarValeAsync(new LancarVale(c.Agenda.ProfissionalId, valor, c.Segunda));
    }

    /// <summary>Quinzena aberta com um atendimento concluído (comissão R$ 20) e um vale de R$ 10, os dois dentro dela.</summary>
    private async Task<Guid> QuinzenaComAtendimentoEValeAsync(Cenario c)
    {
        await SemeadorDeComissoes.SemearEConcluirAsync(_fabrica, c.Admin, c.NegocioId, c.Agenda.ProfissionalId, Local(c, 10));
        await LancarValeAsync(c, 10m);
        using var escopo = Escopo(c.NegocioId);
        var periodo = await Servico<IServicoQuinzenas>(escopo).CriarAsync(c.Segunda.AddDays(-3), c.Segunda.AddDays(3));
        periodo.Sucesso.Should().BeTrue();
        return periodo.PeriodoId!.Value;
    }

    private Task<(List<(decimal Comissao, decimal Vales, decimal Liquido)>, int, string)> QuinzenaAsync(Cenario c, Guid periodo) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var fechamentos = (await db.FechamentosComissao.IgnoreQueryFilters().Where(f => f.PeriodoComissaoId == periodo).ToListAsync())
                .Select(f => (f.TotalComissao, f.TotalVales, f.Liquido)).ToList();
            var quitacoes = await db.QuitacoesSaldo.IgnoreQueryFilters().CountAsync(q => q.NegocioId == c.NegocioId);
            var estado = await db.PeriodosComissao.IgnoreQueryFilters().Where(p => p.Id == periodo).Select(p => p.Estado.ToString()).SingleAsync();
            return (fechamentos, quitacoes, estado);
        });

    private Task<List<decimal>> LancamentosAsync(Cenario c) =>
        _fabrica.NoBancoAsync(db => db.LancamentosSaldoDevedor.IgnoreQueryFilters().Where(l => l.NegocioId == c.NegocioId).Select(l => l.Valor).ToListAsync());

    private Task<List<Plataforma.Dominio.Estoque.VendaProduto>> VendasAsync(Cenario c) =>
        _fabrica.NoBancoAsync(db => db.VendasProduto.IgnoreQueryFilters().AsNoTracking().Where(v => v.NegocioId == c.NegocioId).ToListAsync());

    /// <summary>(quantidade do produto, soma dos movimentos, número de movimentos).</summary>
    private Task<(int, int, int)> EstoqueAsync(Guid produto) =>
        _fabrica.NoBancoAsync(async db => (
            await db.Produtos.IgnoreQueryFilters().Where(p => p.Id == produto).Select(p => p.QuantidadeEstoque).SingleAsync(),
            await db.MovimentosEstoque.IgnoreQueryFilters().Where(m => m.ProdutoId == produto).SumAsync(m => m.Quantidade),
            await db.MovimentosEstoque.IgnoreQueryFilters().CountAsync(m => m.ProdutoId == produto)));

    private Task<List<Agendamento>> AgendamentosEmAsync(Cenario c, DateTimeOffset inicio) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.ProfissionalId == c.Agenda.ProfissionalId && a.Inicio == inicio).ToListAsync());

    private Task<int> AuditoriaAsync(Cenario c, string acao) =>
        _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().CountAsync(l => l.NegocioId == c.NegocioId && l.Acao == acao));

    private Task<int> SelosAsync(Guid agendamentoId) =>
        _fabrica.NoBancoAsync(db => db.SelosCliente.IgnoreQueryFilters().CountAsync(s => s.AgendamentoId == agendamentoId));

    private Task<Agendamento> EstadoAsync(Guid id) =>
        _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AsNoTracking().Include(a => a.Servicos).SingleAsync(a => a.Id == id));

    private IServiceScope Escopo(Guid negocioId)
    {
        var escopo = _fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioId);
        return escopo;
    }

    private static T Servico<T>(IServiceScope escopo) where T : notnull => escopo.ServiceProvider.GetRequiredService<T>();

    private int Avisos(string email) => _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Count(e => e.Destinatario == email);

    /// <summary>Falha transitória (40001) na primeira vez que a operação toca a tabela.</summary>
    private Task<FalhaInjetada> FalharAsync(bool ativa, string tabela, string evento) =>
        ativa ? FalhaInjetada.CriarAsync(_fabrica, tabela, "BEFORE " + evento, deferida: false) : Task.FromResult(FalhaInjetada.Nenhuma);

    /// <summary>Falha transitória (40001) no COMMIT — gatilho de constraint deferido, depois do SaveChanges ter dado certo.</summary>
    private Task<FalhaInjetada> FalharNoCommitAsync(string tabela, string evento) =>
        FalhaInjetada.CriarAsync(_fabrica, tabela, "AFTER " + evento, deferida: true);

    /// <summary>
    /// Gatilho só no banco de teste. DDL não aceita parâmetro no nome do objeto, então o SQL é montado aqui só com valores
    /// do próprio teste (nome aleatório, tabela e evento fixos no código).
    /// </summary>
    private sealed class FalhaInjetada : IAsyncDisposable
    {
        public static readonly FalhaInjetada Nenhuma = new(null, null, null);

        private readonly PlataformaWebApplicationFactory? _fabrica;
        private readonly string? _nome;
        private readonly string? _tabela;

        private FalhaInjetada(PlataformaWebApplicationFactory? fabrica, string? nome, string? tabela)
        {
            _fabrica = fabrica;
            _nome = nome;
            _tabela = tabela;
        }

        public static async Task<FalhaInjetada> CriarAsync(PlataformaWebApplicationFactory fabrica, string tabela, string quando, bool deferida)
        {
            var nome = "retry_falha_" + Guid.NewGuid().ToString("N")[..8];
            var retorno = deferida ? "NULL" : "NEW";
            var gatilho = deferida
                ? "CREATE CONSTRAINT TRIGGER " + nome + " " + quando + " ON " + tabela + " DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION " + nome + "()"
                : "CREATE TRIGGER " + nome + " " + quando + " ON " + tabela + " FOR EACH ROW EXECUTE FUNCTION " + nome + "()";
            await ExecutarAsync(fabrica,
                "CREATE SEQUENCE " + nome,
                "CREATE FUNCTION " + nome + "() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF nextval('" + nome
                    + "') = 1 THEN RAISE EXCEPTION 'falha transitoria simulada' USING ERRCODE = '40001'; END IF; RETURN " + retorno + "; END $$",
                gatilho);
            return new FalhaInjetada(fabrica, nome, tabela);
        }

        /// <summary>Prova que houve nova tentativa: o gatilho disparou na que falhou e de novo na que passou.</summary>
        public async Task ConferirAsync()
        {
            if (_fabrica is null)
                return;

            var sql = "SELECT COALESCE(last_value, 0) AS \"Value\" FROM pg_sequences WHERE sequencename = '" + _nome + "'";
            var disparos = await _fabrica.NoBancoAsync(db => db.Database.SqlQueryRaw<long>(sql).SingleAsync());
            disparos.Should().BeGreaterThanOrEqualTo(2, "a primeira tentativa falhou e a estratégia precisa ter repetido");
        }

        public async ValueTask DisposeAsync()
        {
            if (_fabrica is null)
                return;

            await ExecutarAsync(_fabrica,
                "DROP TRIGGER IF EXISTS " + _nome + " ON " + _tabela, "DROP FUNCTION IF EXISTS " + _nome + "()", "DROP SEQUENCE IF EXISTS " + _nome);
        }

        private static Task ExecutarAsync(PlataformaWebApplicationFactory fabrica, params string[] comandos) =>
            fabrica.NoBancoAsync(async db =>
            {
                foreach (var comando in comandos)
                    await db.Database.ExecuteSqlRawAsync(comando);
                return 0;
            });
    }
}
