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
/// Lembrete 60 min antes (seção 9 — um só desde 2026-09-27; antes eram 24h e 2h) e "lembretes
/// sobrevivem à hibernação da API" (Sprint 4). O job é chamado diretamente (não espera o cron do Hangfire) — o que se testa é
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

    /// <param name="marcadoHa">Há quanto tempo o horário foi combinado. Padrão: um dia (com folga, recebe lembrete).</param>
    private async Task<(Guid NegocioId, Guid AgendamentoId)> SemearAgendamentoAsync(
        DateTimeOffset inicio, StatusAgendamento status, TimeSpan? marcadoHa = null)
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

        typeof(Agendamento).GetProperty(nameof(Agendamento.HorarioCombinadoEm))!
            .SetValue(agendamento, DateTimeOffset.UtcNow - (marcadoHa ?? TimeSpan.FromDays(1)));

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
    public async Task Agendamento_dentro_dos_60_minutos_recebe_lembrete()
    {
        var (_, agendamentoId) = await SemearAgendamentoAsync(DateTimeOffset.UtcNow.AddMinutes(50), StatusAgendamento.Agendado);

        await RodarJobAsync();

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().ContainSingle(e => e.Destinatario == "cliente@teste.com" && e.Assunto.Contains("Lembrete"));

        (await RecarregarAsync(agendamentoId)).LembreteEnviado.Should().BeTrue();
    }

    [Fact]
    public async Task Agendamento_para_daqui_a_mais_de_60_minutos_ainda_nao_recebe_lembrete()
    {
        // O caso que motivou a mudança: marcado para daqui a 23h, o antigo lembrete de 24h saía na hora.
        await SemearAgendamentoAsync(DateTimeOffset.UtcNow.AddHours(23), StatusAgendamento.Agendado);
        await SemearAgendamentoAsync(DateTimeOffset.UtcNow.AddMinutes(70), StatusAgendamento.Agendado);

        await RodarJobAsync();

        _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task Agendamento_marcado_em_cima_da_hora_nao_recebe_lembrete_logo_depois_da_confirmacao()
    {
        // Marcado agora para daqui a 50 min: a janela já está aberta, mas o lembrete chegaria
        // minutos depois da confirmação. É dispensado — e o job não volta a olhar para ele.
        var (_, agendamentoId) = await SemearAgendamentoAsync(DateTimeOffset.UtcNow.AddMinutes(50), StatusAgendamento.Agendado, marcadoHa: TimeSpan.FromMinutes(3));

        await RodarJobAsync();

        _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados.Should().BeEmpty();
        (await RecarregarAsync(agendamentoId)).LembreteEnviado.Should().BeTrue();
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
        await SemearAgendamentoAsync(DateTimeOffset.UtcNow.AddMinutes(45), StatusAgendamento.Agendado);

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

        (await RecarregarAsync(agendamentoId)).LembreteEnviado.Should().BeTrue(); // "resolvido", nunca mais tenta
    }

    private async Task<Agendamento> RecarregarAsync(Guid agendamentoId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        return await dbContext.Agendamentos.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == agendamentoId);
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
