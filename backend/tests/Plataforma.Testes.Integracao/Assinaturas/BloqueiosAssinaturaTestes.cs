using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Api.Controllers.Publico;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Assinaturas;

/// <summary>Bloqueios da suspensão e limite de profissionais, aplicados na API (seção 7 / 8.6.5, ajuste 4b).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class BloqueiosAssinaturaTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public BloqueiosAssinaturaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private enum Situacao { EmTeste, Carencia, Suspensa, TesteVencidoSemJobRodar }

    private async Task DarAssinaturaAsync(Guid negocioId, Situacao situacao, string plano = "Casa Cheia")
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var agora = DateTimeOffset.UtcNow;

        var planoEscolhido = await dbContext.Planos.FirstAsync(p => p.Nome == plano);
        var inicioTeste = situacao switch
        {
            Situacao.EmTeste => agora,
            Situacao.Carencia => agora.AddDays(-32),
            _ => agora.AddDays(-45),
        };

        var assinatura = ServicoAssinatura.IniciarTeste(negocioId, planoEscolhido, Periodicidade.Mensal, "teste", inicioTeste);
        if (situacao != Situacao.TesteVencidoSemJobRodar)
            ServicoAssinatura.AtualizarPorTempo(assinatura, agora);

        dbContext.Assinaturas.Add(assinatura);
        await dbContext.SaveChangesAsync();
    }

    private async Task PagarAsync(Guid negocioId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var agora = DateTimeOffset.UtcNow;

        var assinatura = await dbContext.Assinaturas.IgnoreQueryFilters().Include(a => a.Historico).FirstAsync(a => a.NegocioId == negocioId);
        dbContext.CobrancasAssinatura.Add(ServicoAssinatura.RegistrarPagamento(
            assinatura, assinatura.ValorDoPeriodo, FormaCobranca.Pix, agora, agora, agora.AddMonths(1), OrigemCobranca.Manual, null, "admin", agora));
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Painel_suspenso_responde_402_com_codigo_para_o_front()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, Situacao.Suspensa);

        var resposta = await cliente.GetAsync("/painel/profissionais");

        resposta.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("assinatura_suspensa");
    }

    [Fact]
    public async Task Painel_suspenso_mantem_login_e_dados_de_clientes_para_lgpd()
    {
        var (cliente, negocioId, _, email) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, Situacao.Suspensa);

        using var anonimo = _fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SemeadorDeUsuarios.SenhaPadrao));
        var clientes = await cliente.GetAsync("/painel/clientes");

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        clientes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Painel_em_carencia_funciona_normalmente()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, Situacao.Carencia);

        (await cliente.GetAsync("/painel/profissionais")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Estado_e_decidido_na_hora_mesmo_sem_o_job_ter_rodado()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, Situacao.TesteVencidoSemJobRodar);

        var resposta = await cliente.GetAsync("/painel/profissionais");

        resposta.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        (await dbContext.Assinaturas.IgnoreQueryFilters().SingleAsync(a => a.NegocioId == negocioId)).Estado.Should().Be(EstadoAssinatura.Suspensa);
    }

    [Fact]
    public async Task Pagamento_manual_libera_o_painel_na_hora()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, Situacao.Suspensa);

        await PagarAsync(negocioId);

        (await cliente.GetAsync("/painel/profissionais")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Publico_suspenso_recusa_agendamento_novo_chamando_a_api_direto_sem_revelar_o_motivo()
    {
        var (slug, negocioId, profissionalId, servicoId) = await SemearNegocioComAgendaAsync();
        await DarAssinaturaAsync(negocioId, Situacao.Suspensa);
        using var cliente = ClientePara(slug);

        var reserva = await cliente.PostAsJsonAsync("/publico/reservas", new AgendamentosPublicoController.CriarReservaRequisicao(
            profissionalId, [servicoId], ProximaSegundaAs10()));
        var codigo = await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao("+5571999990000", null));
        var negocio = await cliente.GetFromJsonAsync<JsonElement>("/publico/negocio");

        reserva.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        codigo.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var mensagem = await reserva.Content.ReadAsStringAsync();
        mensagem.Should().NotContainAny("assinatura", "pagamento", "suspens");
        negocio.GetProperty("aceitaAgendamentoOnline").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Publico_suspenso_mantem_agendamento_marcado_cancelamento_por_link_e_lembrete()
    {
        var (slug, negocioId, profissionalId, servicoId) = await SemearNegocioComAgendaAsync();
        var (agendamentoId, token) = await SemearAgendamentoConfirmadoAsync(negocioId, profissionalId, servicoId, DateTimeOffset.UtcNow.AddHours(23));
        await DarAssinaturaAsync(negocioId, Situacao.Suspensa);

        using (var escopo = _fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<JobEnviarLembretes>().ExecutarAsync();

        _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Should().Contain(e => e.Assunto.Contains("Lembrete"));

        using var cliente = ClientePara(slug);
        var cancelar = await cliente.PostAsync($"/publico/meus-agendamentos/{token}/cancelar", null);
        cancelar.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Limite_do_plano_bloqueia_o_profissional_excedente_e_a_reativacao()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, Situacao.EmTeste, plano: "Começo");

        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var criado = await cliente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional($"Profissional {i}", null, null, null, null));
            criado.StatusCode.Should().Be(HttpStatusCode.Created);
            ids.Add(await criado.Content.ReadFromJsonAsync<Guid>());
        }

        var excedente = await cliente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Quarto", null, null, null, null));
        excedente.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = await excedente.Content.ReadAsStringAsync();
        corpo.Should().Contain("limite_profissionais").And.Contain("Começo");

        (await cliente.PostAsync($"/painel/profissionais/{ids[0]}/desativar", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cliente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Substituto", null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await cliente.PostAsync($"/painel/profissionais/{ids[0]}/ativar", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cadastros_simultaneos_nao_furam_o_limite()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await DarAssinaturaAsync(negocioId, Situacao.EmTeste, plano: "Começo");

        var respostas = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            cliente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional($"Paralelo {i}", null, null, null, null))));

        respostas.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(3);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(5);
    }

    private async Task<(string Slug, Guid NegocioId, Guid ProfissionalId, Guid ServicoId)> SemearNegocioComAgendaAsync()
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var slug = "blq-" + Guid.NewGuid().ToString("N")[..12];
        var negocio = Dominio.Negocios.Negocio.Criar(Dominio.Negocios.Slug.Criar(slug), "Negócio Bloqueado", Dominio.Negocios.TipoNegocio.Barbearia);
        dbContext.Negocios.Add(negocio);
        await dbContext.SaveChangesAsync();

        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocio.Id);
        return (slug, negocio.Id, cenario.ProfissionalId, cenario.ServicoId);
    }

    private async Task<(Guid AgendamentoId, string Token)> SemearAgendamentoConfirmadoAsync(Guid negocioId, Guid profissionalId, Guid servicoId, DateTimeOffset inicio)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var cliente = Dominio.Clientes.Cliente.Criar(negocioId, "Cliente Antigo",
            Dominio.Comum.TelefoneE164.Criar($"+55719{Random.Shared.Next(10000000, 99999999)}"), email: "antigo@teste.com");
        dbContext.Clientes.Add(cliente);

        var agendamento = Agendamento.CriarConfirmado(negocioId, profissionalId, cliente.Id, inicio,
            [new ItemServicoAgendamento(servicoId, "Serviço de Teste", 50m, 30)]);
        dbContext.Agendamentos.Add(agendamento);
        await dbContext.SaveChangesAsync();

        var token = escopo.ServiceProvider.GetRequiredService<IServicoTokenPublico>().GerarTokenAgendamento(negocioId, agendamento.Id);
        return (agendamento.Id, token);
    }

    private static DateTimeOffset ProximaSegundaAs10()
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var diasAteSegunda = ((int)DayOfWeek.Monday - (int)hoje.DayOfWeek + 7) % 7;
        var dia = hoje.AddDays(diasAteSegunda == 0 ? 7 : diasAteSegunda);
        return new DateTimeOffset(dia.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Unspecified), TimeSpan.FromHours(-3));
    }

    private HttpClient ClientePara(string slug) => _fabrica.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri($"http://{slug}.{DominioDeTeste.Valor}/"),
    });
}
