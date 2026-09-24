using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Assinaturas;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Assinaturas;

/// <summary>Planos, job de assinaturas e webhook de pagamento (seção 7 / 8.6, ajuste 4a).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AssinaturasTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public AssinaturasTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private async Task<Guid> SemearAssinaturaAsync(Func<Plano, Guid, Assinatura> criar, string emailAdmin = "dono@teste.com")
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var negocio = Negocio.Criar(Slug.Criar("ass-" + Guid.NewGuid().ToString("N")[..12]), "Barbearia do Teste", TipoNegocio.Barbearia);
        dbContext.Negocios.Add(negocio);
        dbContext.Usuarios.Add(Usuario.Criar(negocio.Id, "Dono", emailAdmin, Perfil.Administrador, "hash"));

        var plano = await dbContext.Planos.OrderBy(p => p.Ordem).FirstAsync();
        var assinatura = criar(plano, negocio.Id);
        dbContext.Assinaturas.Add(assinatura);

        await dbContext.SaveChangesAsync();
        return assinatura.Id;
    }

    private async Task<Assinatura> ObterAsync(Guid assinaturaId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        return await dbContext.Assinaturas.IgnoreQueryFilters().Include(a => a.Historico).AsNoTracking().FirstAsync(a => a.Id == assinaturaId);
    }

    private async Task RodarJobAsync()
    {
        using var escopo = _fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<JobAtualizarAssinaturas>().ExecutarAsync();
    }

    [Fact]
    public async Task Migration_semeia_os_tres_planos_com_nomes_e_precos()
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var planos = await dbContext.Planos.OrderBy(p => p.Ordem).ToListAsync();

        planos.Select(p => (p.Nome, p.MinimoProfissionais, p.MaximoProfissionais, p.PrecoMensal, p.PrecoAnualPorMes)).Should().Equal(
            ("Começo", 1, 3, 49.90m, 38.90m),
            ("Ritmo", 4, 7, 79.90m, 68.90m),
            ("Casa Cheia", 8, 12, 99.90m, 88.90m));
        planos.Where(p => p.Destaque).Select(p => p.Nome).Should().Equal("Ritmo");
    }

    [Fact]
    public async Task Job_leva_teste_vencido_ha_muito_tempo_ate_suspensa_e_grava_o_historico()
    {
        var id = await SemearAssinaturaAsync((plano, negocioId) =>
            ServicoAssinatura.IniciarTeste(negocioId, plano, Periodicidade.Mensal, "teste", DateTimeOffset.UtcNow.AddDays(-45)));

        await RodarJobAsync();

        var assinatura = await ObterAsync(id);
        assinatura.Estado.Should().Be(EstadoAssinatura.Suspensa);
        assinatura.Historico.Select(h => h.EstadoNovo).Should().BeEquivalentTo(
            [EstadoAssinatura.EmTeste, EstadoAssinatura.Atrasada, EstadoAssinatura.Suspensa]);
    }

    [Fact]
    public async Task Job_deixa_em_carencia_quem_acabou_o_teste_ha_menos_de_5_dias()
    {
        var id = await SemearAssinaturaAsync((plano, negocioId) =>
            ServicoAssinatura.IniciarTeste(negocioId, plano, Periodicidade.Mensal, "teste", DateTimeOffset.UtcNow.AddDays(-32)));

        await RodarJobAsync();

        (await ObterAsync(id)).Estado.Should().Be(EstadoAssinatura.Atrasada);
    }

    [Fact]
    public async Task Job_avisa_o_administrador_uma_vez_so_por_prazo()
    {
        await SemearAssinaturaAsync((plano, negocioId) =>
            ServicoAssinatura.IniciarTeste(negocioId, plano, Periodicidade.Mensal, "teste", DateTimeOffset.UtcNow.AddDays(-25)));

        await RodarJobAsync();
        await RodarJobAsync();

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().ContainSingle(e => e.Destinatario == "dono@teste.com" && e.Assunto.Contains("teste grátis termina"));
    }

    [Fact]
    public async Task Webhook_de_provedor_sem_webhook_configurado_responde_404()
    {
        using var cliente = _fabrica.CreateClient();

        var resposta = await cliente.PostAsync("/webhooks/pagamentos/manual", new StringContent("{}", Encoding.UTF8, "application/json"));

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Webhook_com_assinatura_invalida_responde_401()
    {
        using var cliente = CriarClienteComGatewayDeTeste();

        var resposta = await cliente.SendAsync(RequisicaoWebhook("evt-1", "sub-x", assinaturaValida: false));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_repetido_e_processado_uma_vez_so()
    {
        var id = await SemearAssinaturaAsync((plano, negocioId) =>
        {
            var assinatura = ServicoAssinatura.IniciarTeste(negocioId, plano, Periodicidade.Mensal, "teste", DateTimeOffset.UtcNow.AddDays(-40));
            ServicoAssinatura.AtualizarPorTempo(assinatura, DateTimeOffset.UtcNow);
            ServicoAssinatura.VincularGateway(assinatura, GatewayDeTeste.Nome, "sub-123");
            return assinatura;
        });

        using var cliente = CriarClienteComGatewayDeTeste();
        var primeira = await cliente.SendAsync(RequisicaoWebhook("evt-42", "sub-123"));
        var segunda = await cliente.SendAsync(RequisicaoWebhook("evt-42", "sub-123"));

        primeira.StatusCode.Should().Be(HttpStatusCode.OK);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);

        var assinatura = await ObterAsync(id);
        assinatura.Estado.Should().Be(EstadoAssinatura.Ativa);

        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        (await dbContext.CobrancasAssinatura.IgnoreQueryFilters().CountAsync(c => c.AssinaturaId == id)).Should().Be(1);
    }

    private HttpClient CriarClienteComGatewayDeTeste() =>
        _fabrica.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IGatewayPagamento, GatewayDeTeste>())).CreateClient();

    private static HttpRequestMessage RequisicaoWebhook(string idEvento, string idAssinatura, bool assinaturaValida = true)
    {
        var corpo = JsonSerializer.Serialize(new { idEvento, idAssinatura });
        var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/pagamentos/{GatewayDeTeste.Nome}")
        {
            Content = new StringContent(corpo, Encoding.UTF8, "application/json"),
        };
        requisicao.Headers.Add("x-assinatura", assinaturaValida ? "valida" : "forjada");
        return requisicao;
    }

    /// <summary>Gateway falso que "confirma com o provedor" só olhando um cabeçalho — basta para exercitar o fluxo.</summary>
    private sealed class GatewayDeTeste : IGatewayPagamento
    {
        public const string Nome = "teste";

        public string Provedor => Nome;

        public bool AceitaWebhook => true;

        public Task<string?> CriarClienteAsync(DadosClienteGateway dados, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> CriarOuAlterarAssinaturaAsync(DadosAssinaturaGateway dados, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task CancelarAssinaturaAsync(string? idExternoAssinatura, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<InstrucoesPagamento> GerarLinkPagamentoAsync(DadosCobrancaGateway dados, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstrucoesPagamento("https://pagar.exemplo", null, null, ""));

        public Task<EventoPagamentoGateway?> InterpretarWebhookAsync(RequisicaoWebhook requisicao, CancellationToken cancellationToken = default)
        {
            if (requisicao.Cabecalhos.GetValueOrDefault("x-assinatura") != "valida")
                return Task.FromResult<EventoPagamentoGateway?>(null);

            using var json = JsonDocument.Parse(requisicao.Corpo);
            var idEvento = json.RootElement.GetProperty("idEvento").GetString()!;
            var idAssinatura = json.RootElement.GetProperty("idAssinatura").GetString()!;
            var agora = DateTimeOffset.UtcNow;

            return Task.FromResult<EventoPagamentoGateway?>(new EventoPagamentoGateway(
                idEvento, TipoEventoPagamento.PagamentoConfirmado, idAssinatura, "cob-" + idEvento, 49.90m, agora, agora, agora.AddMonths(1)));
        }
    }
}
