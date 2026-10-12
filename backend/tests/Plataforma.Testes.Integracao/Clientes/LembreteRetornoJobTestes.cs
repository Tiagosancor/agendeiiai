using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Clientes;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Clientes;

/// <summary>
/// Lembrete de retorno ("faz 30 dias desde o seu último atendimento"). O job é chamado direto com um "agora" controlado
/// (10:05 em Brasília, horário configurado 10:00) — o que se testa é quem recebe, quando e quantas vezes.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class LembreteRetornoJobTestes : IAsyncLifetime
{
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateTimeOffset Agora = Local(Hoje, 10, 5);

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public LembreteRetornoJobTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync()
    {
        Espia.Restaurar();
        await _fabrica.DisposeAsync();
    }

    private EspiaWhatsApp Espia => _fabrica.Services.GetRequiredService<EspiaWhatsApp>();

    [Fact]
    public async Task Concluido_ha_exatamente_30_dias_recebe_e_29_ou_31_nao()
    {
        var negocio = await CriarNegocioAsync();
        var trinta = await CriarClienteAsync(negocio.Id, "Ana");
        var vinteNove = await CriarClienteAsync(negocio.Id, "Bia");
        var trintaEUm = await CriarClienteAsync(negocio.Id, "Caio");
        var referencia = await CriarAgendamentoAsync(negocio.Id, trinta.Id, DiasAtras(30), StatusAgendamento.Concluido);
        await CriarAgendamentoAsync(negocio.Id, vinteNove.Id, DiasAtras(29), StatusAgendamento.Concluido);
        await CriarAgendamentoAsync(negocio.Id, trintaEUm.Id, DiasAtras(31), StatusAgendamento.Concluido);

        await RodarJobAsync(Agora);

        var mensagem = Espia.Enviados.Should().ContainSingle().Subject;
        mensagem.Telefone.Should().Be(trinta.Telefone);
        mensagem.Mensagem.Should()
            .StartWith("Olá, Ana! Já faz 30 dias desde seu último atendimento na Barbearia Retorno. Que tal agendar o próximo? ")
            .And.Contain($"{negocio.Slug.Valor}.{DominioDeTeste.Valor}")
            .And.NotContain("{");

        var envio = (await EnviosAsync()).Should().ContainSingle().Subject;
        (envio.ClienteId, envio.AgendamentoId, envio.Status, envio.Tentativas, envio.DiaEnvio)
            .Should().Be((trinta.Id, referencia, StatusLembreteRetorno.Enviado, 1, Hoje));
    }

    [Theory]
    [InlineData(StatusAgendamento.Faltou)]
    [InlineData(StatusAgendamento.Cancelado)]
    [InlineData(StatusAgendamento.Agendado)]
    public async Task Atendimento_nao_concluido_ha_30_dias_nao_gera_lembrete(StatusAgendamento status)
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), status);

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().BeEmpty();
        (await EnviosAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Falta_depois_do_concluido_nao_muda_a_referencia()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        var concluido = await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(10), StatusAgendamento.Faltou);
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(5), StatusAgendamento.Cancelado);

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().ContainSingle();
        (await EnviosAsync()).Should().ContainSingle(e => e.AgendamentoId == concluido);
    }

    [Fact]
    public async Task Concluido_mais_recente_vira_a_referencia()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(12), StatusAgendamento.Concluido);

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Cliente_com_agendamento_futuro_nao_recebe()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, Agora.AddDays(3), StatusAgendamento.Agendado);

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Cliente_que_nao_aceita_o_lembrete_nao_recebe()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana", aceita: false);
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Cliente_sem_telefone_nao_recebe()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana", comTelefone: false);
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().BeEmpty();
        (await EnviosAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Encaixe_sem_autorizacao_de_mensagens_nao_recebe()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido, OrigemAgendamento.Encaixe);

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Negocio_com_o_lembrete_desligado_nao_envia_nada()
    {
        var negocio = await CriarNegocioAsync(ativo: false);
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().BeEmpty();
        (await EnviosAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Ligar_depois_do_horario_nao_envia_hoje_e_so_vale_a_partir_de_amanha()
    {
        // Ligado às 10:02, depois do disparo das 10:00 de hoje.
        var negocio = await CriarNegocioAsync(ativadoEm: Local(Hoje, 10, 2));
        var deHoje = await CriarClienteAsync(negocio.Id, "Ana");
        var deAmanha = await CriarClienteAsync(negocio.Id, "Bia");
        await CriarAgendamentoAsync(negocio.Id, deHoje.Id, DiasAtras(30), StatusAgendamento.Concluido);
        await CriarAgendamentoAsync(negocio.Id, deAmanha.Id, DiasAtras(29), StatusAgendamento.Concluido);

        await RodarJobAsync(Agora);
        Espia.Enviados.Should().BeEmpty();

        // Amanhã: só quem completa 30 dias amanhã; o de hoje não sai atrasado.
        await RodarJobAsync(Agora.AddDays(1));

        Espia.Enviados.Should().ContainSingle().Which.Telefone.Should().Be(deAmanha.Telefone);
    }

    [Fact]
    public async Task Fora_da_janela_do_dia_nao_envia_nem_depois()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);

        await RodarJobAsync(Local(Hoje, 9, 55)); // antes do horário
        await RodarJobAsync(Local(Hoje, 13, 1)); // janela de 3 h já fechada
        await RodarJobAsync(Agora.AddDays(1));   // dia seguinte: já são 31 dias

        Espia.Enviados.Should().BeEmpty();
        (await EnviosAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Rodar_o_job_duas_vezes_no_mesmo_dia_nao_duplica()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);

        await RodarJobAsync(Agora);
        await RodarJobAsync(Agora.AddMinutes(15));
        await Task.WhenAll(RodarJobAsync(Agora.AddMinutes(30)), RodarJobAsync(Agora.AddMinutes(30)));

        Espia.Enviados.Should().ContainSingle();
        (await EnviosAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Status_alterado_para_falta_entre_a_selecao_e_o_envio_cancela_o_envio()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        var atendimento = await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);

        using var escopo = _fabrica.Services.CreateScope();
        var job = escopo.ServiceProvider.GetRequiredService<JobLembreteRetorno>();
        var selecionados = await job.SelecionarAsync(negocio.Id, Agora);
        selecionados.Should().Equal(atendimento);

        // Corrigido no painel (reaberto e marcado como falta) enquanto o job ainda não tinha enviado.
        await ExecutarSqlAsync($"UPDATE agendamentos SET status = 'Faltou' WHERE id = '{atendimento}'");

        (await job.EnviarAsync(negocio.Id, atendimento, Agora)).Should().BeFalse();

        Espia.Enviados.Should().BeEmpty();
        (await EnviosAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Falha_registra_o_erro_e_tenta_so_mais_uma_vez_no_mesmo_dia()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);
        Espia.Resultado = ResultadoEnvioWhatsApp.Indisponivel;

        await RodarJobAsync(Agora);
        var primeira = (await EnviosAsync()).Should().ContainSingle().Subject;
        (primeira.Status, primeira.Tentativas).Should().Be((StatusLembreteRetorno.Falhou, 1));
        primeira.Erro.Should().NotBeNullOrWhiteSpace();

        await RodarJobAsync(Agora.AddMinutes(15));
        await RodarJobAsync(Agora.AddMinutes(30));

        var depois = (await EnviosAsync()).Should().ContainSingle().Subject;
        (depois.Status, depois.Tentativas).Should().Be((StatusLembreteRetorno.Falhou, 2));

        // Provedor de volta: já não tenta mais.
        Espia.Restaurar();
        await RodarJobAsync(Agora.AddMinutes(45));
        Espia.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Nova_tentativa_no_mesmo_dia_pode_dar_certo()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);

        Espia.Resultado = ResultadoEnvioWhatsApp.Indisponivel;
        await RodarJobAsync(Agora);
        Espia.Restaurar();
        await RodarJobAsync(Agora.AddMinutes(15));

        Espia.Enviados.Should().ContainSingle();
        var envio = (await EnviosAsync()).Should().ContainSingle().Subject;
        (envio.Status, envio.Tentativas, envio.Erro).Should().Be((StatusLembreteRetorno.Enviado, 2, (string?)null));
    }

    [Fact]
    public async Task Falha_de_ontem_nao_e_tentada_hoje()
    {
        var negocio = await CriarNegocioAsync();
        var cliente = await CriarClienteAsync(negocio.Id, "Ana");
        await CriarAgendamentoAsync(negocio.Id, cliente.Id, DiasAtras(30), StatusAgendamento.Concluido);

        // O lembrete passa a ser de 31 dias: amanhã o mesmo atendimento volta a ser "o dia" — mas a falha é de hoje.
        Espia.Resultado = ResultadoEnvioWhatsApp.Indisponivel;
        await RodarJobAsync(Agora);
        Espia.Restaurar();
        await ExecutarSqlAsync($"UPDATE negocios SET lembrete_retorno_dias = 31 WHERE id = '{negocio.Id}'");

        await RodarJobAsync(Agora.AddDays(1));

        Espia.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Um_negocio_nunca_aciona_clientes_de_outro()
    {
        var ligado = await CriarNegocioAsync();
        var desligado = await CriarNegocioAsync(ativo: false);
        var doLigado = await CriarClienteAsync(ligado.Id, "Ana");
        var doDesligado = await CriarClienteAsync(desligado.Id, "Bia");
        await CriarAgendamentoAsync(ligado.Id, doLigado.Id, DiasAtras(30), StatusAgendamento.Concluido);
        await CriarAgendamentoAsync(desligado.Id, doDesligado.Id, DiasAtras(30), StatusAgendamento.Concluido);

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().ContainSingle().Which.Mensagem.Should().Contain("Ana").And.Contain(ligado.Slug.Valor);
        (await EnviosAsync()).Should().OnlyContain(e => e.NegocioId == ligado.Id);

        // Mesmo chamando o negócio ligado com um atendimento do outro, nada sai.
        var atendimentoDoOutro = await CriarAgendamentoAsync(desligado.Id, doDesligado.Id, DiasAtras(30).AddHours(1), StatusAgendamento.Concluido);
        using var escopo = _fabrica.Services.CreateScope();
        var job = escopo.ServiceProvider.GetRequiredService<JobLembreteRetorno>();
        (await job.EnviarAsync(ligado.Id, atendimentoDoOutro, Agora.AddMinutes(1))).Should().BeFalse();
    }

    [Fact]
    public async Task Agendamento_feito_ate_7_dias_depois_do_envio_conta_como_gerado()
    {
        var negocio = await CriarNegocioAsync();
        var voltou = await CriarClienteAsync(negocio.Id, "Ana");
        var demorou = await CriarClienteAsync(negocio.Id, "Bia");
        await CriarAgendamentoAsync(negocio.Id, voltou.Id, DiasAtras(30), StatusAgendamento.Concluido);
        await CriarAgendamentoAsync(negocio.Id, demorou.Id, DiasAtras(30), StatusAgendamento.Concluido);
        await RodarJobAsync(Agora);

        var novo = await CriarAgendamentoAsync(negocio.Id, voltou.Id, Agora.AddDays(10), StatusAgendamento.Agendado);
        var tarde = await CriarAgendamentoAsync(negocio.Id, demorou.Id, Agora.AddDays(20), StatusAgendamento.Agendado);
        await ExecutarSqlAsync($"UPDATE agendamentos SET criado_em = '{Agora.AddDays(3).UtcDateTime:O}' WHERE id = '{novo}'");
        await ExecutarSqlAsync($"UPDATE agendamentos SET criado_em = '{Agora.AddDays(8).UtcDateTime:O}' WHERE id = '{tarde}'");

        await RodarJobAsync(Agora.AddDays(8));

        var envios = await EnviosAsync();
        envios.Single(e => e.ClienteId == voltou.Id).AgendamentoGeradoId.Should().Be(novo);
        envios.Single(e => e.ClienteId == demorou.Id).AgendamentoGeradoId.Should().BeNull();
    }

    // ---------- arranjo ----------

    private static DateTimeOffset Local(DateOnly dia, int hora, int minuto) =>
        new DateTimeOffset(dia.ToDateTime(new TimeOnly(hora, minuto)), Brasilia).ToUniversalTime();

    private static DateTimeOffset DiasAtras(int dias) => Local(Hoje.AddDays(-dias), 14, 0);

    private async Task RodarJobAsync(DateTimeOffset agora)
    {
        using var escopo = _fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<JobLembreteRetorno>().ExecutarAsync(agora);
    }

    private async Task<Negocio> CriarNegocioAsync(bool ativo = true, DateTimeOffset? ativadoEm = null)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var negocio = Negocio.Criar(Slug.Criar("ret-" + Guid.NewGuid().ToString("N")[..12]), "Barbearia Retorno", TipoNegocio.Barbearia);
        negocio.ConfigurarLembreteRetorno(true, 30, new TimeOnly(10, 0), LembreteRetorno.MensagemPadrao, ativadoEm ?? Agora.AddDays(-90));
        if (!ativo)
            negocio.ConfigurarLembreteRetorno(false, 30, new TimeOnly(10, 0), LembreteRetorno.MensagemPadrao, Agora.AddDays(-1));

        dbContext.Negocios.Add(negocio);
        await dbContext.SaveChangesAsync();
        return negocio;
    }

    private async Task<Cliente> CriarClienteAsync(Guid negocioId, string nome, bool aceita = true, bool comTelefone = true)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var telefone = comTelefone ? TelefoneE164.Criar($"+55719{Random.Shared.Next(10000000, 99999999)}") : null;
        var cliente = Cliente.Criar(negocioId, nome, telefone);
        cliente.DefinirAceitaLembreteRetorno(aceita);

        dbContext.Clientes.Add(cliente);
        await dbContext.SaveChangesAsync();
        return cliente;
    }

    private async Task<Guid> CriarAgendamentoAsync(
        Guid negocioId, Guid clienteId, DateTimeOffset inicio, StatusAgendamento status, OrigemAgendamento origem = OrigemAgendamento.Painel)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var servicos = new[] { new ItemServicoAgendamento(Guid.NewGuid(), "Corte", 50m, 30) };
        var agendamento = Agendamento.CriarConfirmado(negocioId, Guid.NewGuid(), clienteId, inicio, servicos, origem: origem);

        switch (status)
        {
            case StatusAgendamento.Concluido:
                agendamento.MarcarConcluido(0m, inicio);
                break;
            case StatusAgendamento.Faltou:
                agendamento.MarcarFaltou();
                break;
            case StatusAgendamento.Cancelado:
                agendamento.Cancelar();
                break;
        }

        dbContext.Agendamentos.Add(agendamento);
        await dbContext.SaveChangesAsync();
        return agendamento.Id;
    }

    private async Task<List<LembreteRetornoEnvio>> EnviosAsync()
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        return await dbContext.LembretesRetornoEnvios.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    private async Task ExecutarSqlAsync(string sql)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(sql);
    }
}
