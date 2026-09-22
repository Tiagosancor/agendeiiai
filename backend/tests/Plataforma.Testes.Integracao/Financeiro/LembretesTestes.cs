using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Financeiro;

/// <summary>
/// Lembretes 24h/2h antes (seção 9, Sprint 4, M: "lembretes sobrevivem à hibernação da
/// API"). O job é chamado diretamente (não espera o cron do Hangfire) — o que se testa é
/// a lógica de "quem precisa de lembrete agora", que é a mesma se o job atrasou 5 minutos
/// ou 5 dias por causa da API estar hibernando.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class LembretesTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public LembretesTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private async Task<(Guid NegocioId, Guid AgendamentoId)> SemearAgendamentoAsync(DateTimeOffset inicio, StatusAgendamento status)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var negocio = Negocio.Criar(Slug.Criar("lemb-" + Guid.NewGuid().ToString("N")[..12]), "Negócio de Teste", TipoNegocio.Barbearia);
        dbContext.Negocios.Add(negocio);

        var cliente = Cliente.Criar(negocio.Id, "Cliente de Teste", TelefoneE164.Criar($"+55719{Random.Shared.Next(10000000, 99999999)}"), email: "cliente@teste.com");
        dbContext.Clientes.Add(cliente);

        var servicos = new[] { new ItemServicoAgendamento(Guid.NewGuid(), "Corte", 50m, 30) };
        var agendamento = Agendamento.CriarConfirmado(negocio.Id, Guid.NewGuid(), cliente.Id, inicio, servicos);

        if (status == StatusAgendamento.Cancelado)
            agendamento.Cancelar();

        dbContext.Agendamentos.Add(agendamento);
        await dbContext.SaveChangesAsync();

        return (negocio.Id, agendamento.Id);
    }

    private async Task RodarJobAsync()
    {
        using var escopo = _fabrica.Services.CreateScope();
        var job = escopo.ServiceProvider.GetRequiredService<JobEnviarLembretes>();
        await job.ExecutarAsync();
    }

    [Fact]
    public async Task Agendamento_dentro_da_janela_de_24h_recebe_lembrete()
    {
        var (_, agendamentoId) = await SemearAgendamentoAsync(DateTimeOffset.UtcNow.AddHours(23), StatusAgendamento.Agendado);

        await RodarJobAsync();

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().ContainSingle(e => e.Destinatario == "cliente@teste.com" && e.Assunto.Contains("Lembrete"));

        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var agendamento = await dbContext.Agendamentos.IgnoreQueryFilters().FirstAsync(a => a.Id == agendamentoId);
        agendamento.Lembrete24hEnviado.Should().BeTrue();
        agendamento.Lembrete2hEnviado.Should().BeFalse();
    }

    [Fact]
    public async Task Agendamento_fora_da_janela_nao_recebe_lembrete()
    {
        await SemearAgendamentoAsync(DateTimeOffset.UtcNow.AddHours(48), StatusAgendamento.Agendado);

        await RodarJobAsync();

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Rodar_o_job_duas_vezes_nao_envia_lembrete_duplicado()
    {
        await SemearAgendamentoAsync(DateTimeOffset.UtcNow.AddHours(23), StatusAgendamento.Agendado);

        await RodarJobAsync();
        await RodarJobAsync();

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().ContainSingle();
    }

    [Fact]
    public async Task Agendamento_ja_vencido_e_descartado_sem_enviar()
    {
        // Simula a API tendo ficado fora do ar: o agendamento já devia ter tido lembrete,
        // mas o horário já passou (seção 8.5.6 — descarta os vencidos).
        var (negocioId, agendamentoId) = await SemearAgendamentoAsync(DateTimeOffset.UtcNow.AddHours(-1), StatusAgendamento.Agendado);

        await RodarJobAsync();

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().BeEmpty();

        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var agendamento = await dbContext.Agendamentos.IgnoreQueryFilters().FirstAsync(a => a.Id == agendamentoId);
        agendamento.Lembrete24hEnviado.Should().BeTrue(); // marcado como "resolvido", nunca mais tenta
        agendamento.Lembrete2hEnviado.Should().BeTrue();
    }

    [Fact]
    public async Task Agendamento_cancelado_nao_recebe_lembrete()
    {
        await SemearAgendamentoAsync(DateTimeOffset.UtcNow.AddHours(1), StatusAgendamento.Cancelado);

        await RodarJobAsync();

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().BeEmpty();
    }
}
