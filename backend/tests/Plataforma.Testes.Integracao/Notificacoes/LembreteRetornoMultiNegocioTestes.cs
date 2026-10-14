using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Assinaturas;
using Plataforma.Infraestrutura.Clientes;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Notificacoes;

/// <summary>
/// B7.4-A: o lembrete de retorno também varre vários negócios por execução. Antes, só o primeiro era atendido (o contexto de negócio
/// scoped não troca de tenant e o erro dos demais caía num log). Agora cada negócio roda num escopo próprio, com a sua configuração,
/// o seu fuso e os seus dados. "Agora" é fixo: 13:05 UTC, depois do horário configurado em cada fuso — nada depende do relógio.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class LembreteRetornoMultiNegocioTestes : IAsyncLifetime
{
    private const string Pii = "maria.segredo@exemplo.com 71988880000 Maria Segredo";
    private static readonly DateTimeOffset Agora = new(2026, 10, 14, 13, 5, 0, TimeSpan.Zero);

    private readonly PostgresContainerFixture _postgres;
    private readonly RegistroDeContextos _contextos = new();
    private readonly CapturaDeLogs _logs = new();
    private AssinaturaQueFalhaParaNegocio _assinatura = null!;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public LembreteRetornoMultiNegocioTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, null, servicos =>
        {
            servicos.AdicionarEspioes(_contextos, _logs);
            servicos.AddScoped<IConsultaSituacaoAssinatura>(sp =>
            {
                var real = ActivatorUtilities.CreateInstance<ConsultaSituacaoAssinatura>(sp);
                var alvo = sp.GetRequiredService<AssinaturaQueFalhaParaNegocio>();
                return new DecoradorAssinatura(real, alvo);
            });
            servicos.AddSingleton(sp => _assinatura = new AssinaturaQueFalhaParaNegocio(null!));
        });
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
        _ = _fabrica.Services.GetRequiredService<AssinaturaQueFalhaParaNegocio>();
        _fabrica.Services.GetRequiredService<EspiaWhatsApp>().Restaurar();
        _fabrica.Services.GetRequiredService<EspiaWhatsApp>().Enviados.Clear();
    }

    public async Task DisposeAsync()
    {
        _fabrica.Services.GetRequiredService<EspiaWhatsApp>().Restaurar();
        await _fabrica.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    /// <summary>Consulta de assinatura de verdade, exceto para o negócio-alvo, que falha com uma mensagem cheia de dado pessoal.</summary>
    private sealed class DecoradorAssinatura(IConsultaSituacaoAssinatura real, AssinaturaQueFalhaParaNegocio alvo) : IConsultaSituacaoAssinatura
    {
        public Task<SituacaoAssinatura?> ObterAsync(Guid negocioId, CancellationToken cancellationToken = default) =>
            negocioId == alvo.NegocioAlvo ? throw new InvalidOperationException(alvo.Mensagem) : real.ObterAsync(negocioId, cancellationToken);
    }

    private sealed record Semeado(Guid NegocioId, string Nome, string Slug, string Telefone, string Marca);

    /// <summary>Negócio com horário, fuso, prazo (dias) e mensagem próprios, e um cliente cujo último atendimento foi há exatamente "dias" dias.</summary>
    private async Task<Semeado> SemearAsync(
        string nome, string fuso, TimeOnly horario, int dias, string mensagem, bool ligado = true, string telefone = "", int? diasConfigurados = null)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var tz = TimeZoneInfo.FindSystemTimeZoneById(fuso);

        var slug = "ret-" + Guid.NewGuid().ToString("N")[..12];
        var negocio = Negocio.Criar(Slug.Criar(slug), nome, TipoNegocio.Barbearia, fuso);
        negocio.ConfigurarLembreteRetorno(ligado, diasConfigurados ?? dias, horario, mensagem, Agora.AddDays(-90));
        db.Negocios.Add(negocio);

        telefone = telefone.Length > 0 ? telefone : $"+55719{Random.Shared.Next(10000000, 99999999)}";
        var cliente = Cliente.Criar(negocio.Id, $"Cliente {nome}", TelefoneE164.Criar(telefone));
        db.Clientes.Add(cliente);

        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Agora, tz).DateTime);
        var atendimento = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(hoje.AddDays(-dias).ToDateTime(new TimeOnly(14, 0)), tz), TimeSpan.Zero);
        var agendamento = Agendamento.CriarConfirmado(negocio.Id, Guid.NewGuid(), cliente.Id, atendimento,
            [new ItemServicoAgendamento(Guid.NewGuid(), "Corte", 50m, 30)]);
        agendamento.MarcarConcluido(0m, atendimento);
        db.Agendamentos.Add(agendamento);
        await db.SaveChangesAsync();

        return new Semeado(negocio.Id, nome, slug, cliente.Telefone!.Valor, $"Marca-{nome}");
    }

    private async Task RodarJobAsync(DateTimeOffset agora)
    {
        using var escopo = _fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<JobLembreteRetorno>().ExecutarAsync(agora);
    }

    private async Task<List<LembreteRetornoEnvio>> EnviosAsync()
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        return await db.LembretesRetornoEnvios.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    private EspiaWhatsApp Espia => _fabrica.Services.GetRequiredService<EspiaWhatsApp>();

    // São Paulo (UTC-3): 10:00 local = 13:00 UTC. Manaus (UTC-4): 09:00 local = 13:00 UTC. As duas janelas estão abertas às 13:05 UTC.
    private Task<Semeado> AlfaAsync(bool ligado = true) =>
        SemearAsync("Barbearia Alfa", "America/Sao_Paulo", new TimeOnly(10, 0), 30, "Oi {nome_cliente}! A Alfa sente a sua falta há {dias} dias: {link_agendamento}", ligado);

    private Task<Semeado> BetaAsync() =>
        SemearAsync("Studio Beta", "America/Manaus", new TimeOnly(9, 0), 45, "Olá {nome_cliente}, o Beta espera você depois de {dias} dias: {link_agendamento}");

    [Fact]
    public async Task Dois_negocios_ativos_e_elegiveis_recebem_cada_um_a_sua_mensagem()
    {
        var alfa = await AlfaAsync();
        var beta = await BetaAsync();

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().HaveCount(2);
        var paraAlfa = Espia.Enviados.Should().ContainSingle(e => e.Telefone.Valor == alfa.Telefone).Subject.Mensagem;
        var paraBeta = Espia.Enviados.Should().ContainSingle(e => e.Telefone.Valor == beta.Telefone).Subject.Mensagem;

        // Configuração, prazo e link de cada tenant — nada cruza.
        paraAlfa.Should().Contain("A Alfa").And.Contain("30 dias").And.Contain(alfa.Slug).And.NotContain("Beta").And.NotContain(beta.Slug);
        paraBeta.Should().Contain("o Beta").And.Contain("45 dias").And.Contain(beta.Slug).And.NotContain("Alfa").And.NotContain(alfa.Slug);

        var envios = await EnviosAsync();
        envios.Should().HaveCount(2);
        envios.Should().OnlyContain(e => e.Status == StatusLembreteRetorno.Enviado);
        envios.Select(e => e.NegocioId).Should().BeEquivalentTo([alfa.NegocioId, beta.NegocioId]);
    }

    [Fact]
    public async Task Segunda_execucao_no_mesmo_dia_nao_duplica_nenhum_dos_dois()
    {
        await AlfaAsync();
        await BetaAsync();

        await RodarJobAsync(Agora);
        await RodarJobAsync(Agora.AddMinutes(15));
        await RodarJobAsync(Agora.AddMinutes(30));

        Espia.Enviados.Should().HaveCount(2);
        (await EnviosAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Cada_negocio_roda_em_um_escopo_proprio_e_nenhum_escopo_troca_de_tenant()
    {
        var alfa = await AlfaAsync();
        var beta = await BetaAsync();
        _contextos.Definicoes.Clear();

        await RodarJobAsync(Agora);

        // O escopo do job nunca define negócio; cada escopo por negócio define o seu uma única vez.
        _contextos.Definicoes.GroupBy(d => d.Instancia).Should().OnlyContain(g => g.Select(d => d.Negocio).Distinct().Count() == 1);
        _contextos.Definicoes.Select(d => d.Negocio).Distinct().Should().BeEquivalentTo([alfa.NegocioId, beta.NegocioId]);
    }

    [Fact]
    public async Task Negocio_desligado_ao_lado_de_ativos_nao_recebe_nem_atrapalha()
    {
        var desligado = await AlfaAsync(ligado: false);
        var beta = await BetaAsync();

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().ContainSingle().Which.Telefone.Valor.Should().Be(beta.Telefone);
        (await EnviosAsync()).Should().OnlyContain(e => e.NegocioId != desligado.NegocioId);
    }

    [Fact]
    public async Task Janela_de_cada_negocio_respeita_o_fuso_dele()
    {
        var alfa = await AlfaAsync();
        var beta = await BetaAsync();

        // 12:50 UTC = 09:50 em São Paulo (antes das 10:00) e 08:50 em Manaus (antes das 09:00): ninguém ainda.
        await RodarJobAsync(Agora.AddMinutes(-15));
        Espia.Enviados.Should().BeEmpty();

        // 13:05 UTC: as duas janelas abriram (10:05 e 09:05 locais).
        await RodarJobAsync(Agora);
        Espia.Enviados.Select(e => e.Telefone.Valor).Should().BeEquivalentTo([alfa.Telefone, beta.Telefone]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Falha_em_um_negocio_nao_impede_o_outro_qualquer_que_seja_a_ordem(bool falhaOPrimeiroDaOrdem)
    {
        var alfa = await AlfaAsync();
        var beta = await BetaAsync();
        var ordenados = new[] { alfa, beta }.OrderBy(x => x.NegocioId).ToList();
        var (quebrado, saudavel) = falhaOPrimeiroDaOrdem ? (ordenados[0], ordenados[1]) : (ordenados[1], ordenados[0]);
        _assinatura.NegocioAlvo = quebrado.NegocioId;
        _assinatura.Mensagem = $"erro de banco com {Pii}";

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().ContainSingle().Which.Telefone.Valor.Should().Be(saudavel.Telefone);
        (await EnviosAsync()).Should().ContainSingle().Which.NegocioId.Should().Be(saudavel.NegocioId);

        // O log traz o id técnico e o tipo do erro, nunca a mensagem da exceção (que pode carregar dado pessoal).
        var linhas = string.Join('\n', _logs.Linhas);
        linhas.Should().Contain(quebrado.NegocioId.ToString()).And.Contain(nameof(InvalidOperationException));
        linhas.Should().NotContain("maria.segredo").And.NotContain("71988880000").And.NotContain("Maria Segredo");
        linhas.Should().NotContain(alfa.Telefone).And.NotContain(beta.Telefone);

        // Execução seguinte: o negócio que falhou é tratado e o que já tinha saído não se repete.
        _assinatura.NegocioAlvo = null;
        await RodarJobAsync(Agora.AddMinutes(15));

        Espia.Enviados.Should().HaveCount(2);
        Espia.Enviados.Should().ContainSingle(e => e.Telefone.Valor == quebrado.Telefone);
        (await EnviosAsync()).Should().HaveCount(2).And.OnlyContain(e => e.Status == StatusLembreteRetorno.Enviado);
    }

    [Fact]
    public async Task Um_unico_negocio_continua_funcionando()
    {
        var alfa = await AlfaAsync();

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().ContainSingle().Which.Telefone.Valor.Should().Be(alfa.Telefone);
        (await EnviosAsync()).Should().ContainSingle().Which.Status.Should().Be(StatusLembreteRetorno.Enviado);
    }

    [Fact]
    public async Task Negocio_ligado_sem_itens_elegiveis_nao_atrapalha_quem_tem()
    {
        var alfa = await AlfaAsync();
        // Prazo de 31 dias, mas o último atendimento do cliente foi há 30: nada coincide hoje.
        await SemearAsync("Studio Sem Elegiveis", "America/Sao_Paulo", new TimeOnly(10, 0), 30, "Oi {nome_cliente} {dias} {link_agendamento}", diasConfigurados: 31);

        await RodarJobAsync(Agora);

        Espia.Enviados.Should().ContainSingle().Which.Telefone.Valor.Should().Be(alfa.Telefone);
    }
}
