using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Infraestrutura.Seguranca;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Notificacoes;

/// <summary>
/// B7.4-A: o job de lembrete de 60 minutos varre todos os negócios na mesma execução. Cada negócio roda num escopo de DI próprio
/// (o contexto de negócio é scoped e não troca de tenant): com dois negócios na mesma janela o job antigo lançava no segundo,
/// reenviava o primeiro a cada rodada e nunca atendia o segundo.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class LembretesMultiNegocioTestes : IAsyncLifetime
{
    private const string Pii = "joao.segredo@exemplo.com 71999990000 Joao Segredo";

    private readonly PostgresContainerFixture _postgres;
    private readonly RegistroDeContextos _contextos = new();
    private readonly CapturaDeLogs _logs = new();
    private TokenQueFalhaParaNegocio _token = null!;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public LembretesMultiNegocioTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, null, servicos =>
        {
            servicos.AdicionarEspioes(_contextos, _logs);
            servicos.AddSingleton<IServicoTokenPublico>(sp =>
                _token = new TokenQueFalhaParaNegocio(ActivatorUtilities.CreateInstance<ServicoTokenPublico>(sp)));
        });
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
        _ = _fabrica.Services.GetRequiredService<IServicoTokenPublico>();
    }

    public async Task DisposeAsync()
    {
        await _fabrica.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    private sealed record Semeado(Guid NegocioId, Guid AgendamentoId, string Nome, string EmailCliente);

    private async Task<Semeado> SemearAsync(string nomeNegocio, TimeSpan inicioEm, StatusAgendamento status = StatusAgendamento.Agendado)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var negocio = Negocio.Criar(Slug.Criar("lemb-" + Guid.NewGuid().ToString("N")[..12]), nomeNegocio, TipoNegocio.Barbearia);
        db.Negocios.Add(negocio);

        var email = $"cliente-{Guid.NewGuid():N}@teste.com";
        var cliente = Cliente.Criar(negocio.Id, "Cliente de Teste", TelefoneE164.Criar($"+55719{Random.Shared.Next(10000000, 99999999)}"), email: email);
        db.Clientes.Add(cliente);

        var servicos = new[] { new ItemServicoAgendamento(Guid.NewGuid(), "Corte", 50m, 30) };
        var agendamento = Agendamento.CriarConfirmado(negocio.Id, Guid.NewGuid(), cliente.Id, DateTimeOffset.UtcNow + inicioEm, servicos);
        if (status == StatusAgendamento.Cancelado)
            agendamento.Cancelar();
        typeof(Agendamento).GetProperty(nameof(Agendamento.HorarioCombinadoEm))!.SetValue(agendamento, DateTimeOffset.UtcNow.AddDays(-1));
        db.Agendamentos.Add(agendamento);
        await db.SaveChangesAsync();

        return new Semeado(negocio.Id, agendamento.Id, nomeNegocio, email);
    }

    private async Task RodarJobAsync()
    {
        using var escopo = _fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<JobEnviarLembretes>().ExecutarAsync();
    }

    private async Task<bool> MarcadoAsync(Guid agendamentoId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        return (await db.Agendamentos.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == agendamentoId)).LembreteEnviado;
    }

    private EspiaEmail Email => _fabrica.Services.GetRequiredService<EspiaEmail>();

    [Fact]
    public async Task Dois_negocios_elegiveis_na_mesma_execucao_recebem_um_lembrete_cada_e_ficam_marcados()
    {
        var a = await SemearAsync("Barbearia Alfa", TimeSpan.FromMinutes(45));
        var b = await SemearAsync("Salão Beta", TimeSpan.FromMinutes(50));

        await RodarJobAsync();

        Email.Enviados.Should().HaveCount(2);
        var paraA = Email.Enviados.Should().ContainSingle(e => e.Destinatario == a.EmailCliente).Subject;
        var paraB = Email.Enviados.Should().ContainSingle(e => e.Destinatario == b.EmailCliente).Subject;
        (await MarcadoAsync(a.AgendamentoId)).Should().BeTrue();
        (await MarcadoAsync(b.AgendamentoId)).Should().BeTrue();

        // Isolamento: o e-mail de cada cliente fala só do próprio negócio.
        paraA.Assunto.Should().Contain("Barbearia Alfa").And.NotContain("Salão Beta");
        paraB.Assunto.Should().Contain("Salão Beta").And.NotContain("Barbearia Alfa");
        paraA.CorpoHtml.Should().Contain("Barbearia Alfa").And.NotContain("Salão Beta");
        paraB.CorpoHtml.Should().Contain("Salão Beta").And.NotContain("Barbearia Alfa");
    }

    [Fact]
    public async Task Rodar_de_novo_nao_duplica_nenhum_dos_dois()
    {
        await SemearAsync("Barbearia Alfa", TimeSpan.FromMinutes(45));
        await SemearAsync("Salão Beta", TimeSpan.FromMinutes(50));

        await RodarJobAsync();
        await RodarJobAsync();
        await RodarJobAsync();

        Email.Enviados.Should().HaveCount(2);
    }

    [Fact]
    public async Task Cada_negocio_roda_em_um_escopo_proprio_e_nenhum_escopo_troca_de_tenant()
    {
        var a = await SemearAsync("Barbearia Alfa", TimeSpan.FromMinutes(45));
        var b = await SemearAsync("Salão Beta", TimeSpan.FromMinutes(50));
        var c = await SemearAsync("Studio Gama", TimeSpan.FromMinutes(55));

        await RodarJobAsync();

        var porEscopo = _contextos.Definicoes.GroupBy(d => d.Instancia).ToList();
        porEscopo.Should().HaveCount(3, "um escopo por negócio; o escopo do job nunca define negócio");
        porEscopo.Should().OnlyContain(g => g.Count() == 1, "cada escopo define o seu negócio uma única vez");
        _contextos.Definicoes.Select(d => d.Negocio).Should().BeEquivalentTo([a.NegocioId, b.NegocioId, c.NegocioId]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Falha_em_um_negocio_nao_impede_o_outro_qualquer_que_seja_a_ordem(bool falhaOPrimeiroDaOrdem)
    {
        var a = await SemearAsync("Barbearia Alfa", TimeSpan.FromMinutes(45));
        var b = await SemearAsync("Salão Beta", TimeSpan.FromMinutes(50));
        var ordenados = new[] { a, b }.OrderBy(x => x.NegocioId).ToList();
        var (quebrado, saudavel) = falhaOPrimeiroDaOrdem ? (ordenados[0], ordenados[1]) : (ordenados[1], ordenados[0]);
        _token.NegocioAlvo = quebrado.NegocioId;
        _token.Mensagem = $"erro de banco com {Pii}";

        await RodarJobAsync();

        Email.Enviados.Should().ContainSingle(e => e.Destinatario == saudavel.EmailCliente);
        Email.Enviados.Should().NotContain(e => e.Destinatario == quebrado.EmailCliente);
        (await MarcadoAsync(saudavel.AgendamentoId)).Should().BeTrue();
        (await MarcadoAsync(quebrado.AgendamentoId)).Should().BeFalse("falhou antes de enviar: continua pendente");

        // O log traz o id técnico e o tipo do erro, nunca a mensagem da exceção (que pode carregar dado pessoal).
        var linhas = string.Join('\n', _logs.Linhas);
        linhas.Should().Contain(quebrado.NegocioId.ToString()).And.Contain(nameof(InvalidOperationException));
        linhas.Should().NotContain("joao.segredo").And.NotContain("71999990000").And.NotContain("Joao Segredo");
        linhas.Should().NotContain(saudavel.EmailCliente).And.NotContain(quebrado.EmailCliente);

        // Na rodada seguinte o negócio que falhou é tratado, sem repetir o que já saiu.
        _token.NegocioAlvo = null;
        await RodarJobAsync();

        Email.Enviados.Should().HaveCount(2);
        Email.Enviados.Should().ContainSingle(e => e.Destinatario == quebrado.EmailCliente);
        (await MarcadoAsync(quebrado.AgendamentoId)).Should().BeTrue();
    }

    [Fact]
    public async Task Negocios_sem_itens_elegiveis_nao_atrapalham_quem_tem()
    {
        var foraDaJanela = await SemearAsync("Fora da Janela", TimeSpan.FromHours(5));
        var cancelado = await SemearAsync("Cancelado", TimeSpan.FromMinutes(40), StatusAgendamento.Cancelado);
        var elegivel = await SemearAsync("Elegível", TimeSpan.FromMinutes(45));

        await RodarJobAsync();

        Email.Enviados.Should().ContainSingle().Which.Destinatario.Should().Be(elegivel.EmailCliente);
        (await MarcadoAsync(foraDaJanela.AgendamentoId)).Should().BeFalse();
        (await MarcadoAsync(cancelado.AgendamentoId)).Should().BeFalse();
        _contextos.Definicoes.Select(d => d.Negocio).Should().BeEquivalentTo([elegivel.NegocioId], "só o negócio com item elegível ganha escopo");
    }

    [Fact]
    public async Task Um_unico_negocio_continua_funcionando()
    {
        var unico = await SemearAsync("Barbearia Única", TimeSpan.FromMinutes(45));

        await RodarJobAsync();

        Email.Enviados.Should().ContainSingle().Which.Destinatario.Should().Be(unico.EmailCliente);
        (await MarcadoAsync(unico.AgendamentoId)).Should().BeTrue();
    }
}
