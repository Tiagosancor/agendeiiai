using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Notificacoes.Outbox;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Notificacoes.Outbox;

/// <summary>Relógio do despachante e dos jobs da outbox: o tempo real mais um deslocamento que o teste controla (backoff, lease, prazo).</summary>
public sealed class RelogioDeTeste : TimeProvider
{
    private long _deslocamentoTicks;

    public TimeSpan Deslocamento
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _deslocamentoTicks));
        set => Interlocked.Exchange(ref _deslocamentoTicks, value.Ticks);
    }

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Deslocamento;

    public void Avancar(TimeSpan quanto) => Deslocamento += quanto;
}

public sealed record CenarioOutbox(
    HttpClient Admin, Guid NegocioId, Guid ProfissionalId, Guid ServicoId, Guid ClienteId, DateTimeOffset Segunda,
    string EmailCliente, string EmailProfissional, string TelefoneProfissional);

/// <summary>
/// Base dos testes da outbox: API inteira com a outbox ligada (todas as famílias), relógio controlável, interceptador de COMMIT e os
/// espiões de e-mail e WhatsApp. Cada teste usa o seu banco limpo.
/// </summary>
public abstract class OutboxTestesBase : IAsyncLifetime
{
    protected const string Pii = "Fulano Sigiloso fulano.sigiloso@exemplo.com 71999990000";

    protected OutboxTestesBase(PostgresContainerFixture postgres) => Postgres = postgres;

    protected PostgresContainerFixture Postgres { get; }

    protected RelogioDeTeste Relogio { get; } = new();

    protected InterceptadorDeCommit Commit { get; } = new();

    protected PlataformaWebApplicationFactory Fabrica { get; private set; } = null!;

    protected EspiaEmail Email => Fabrica.Services.GetRequiredService<EspiaEmail>();

    protected EspiaWhatsApp WhatsApp => Fabrica.Services.GetRequiredService<EspiaWhatsApp>();

    /// <summary>Chaves da outbox. Por padrão tudo ligado; os testes de rollout sobrepõem.</summary>
    protected virtual Dictionary<string, string?> Configuracao => new()
    {
        ["Notificacoes:OutboxHabilitada"] = "true",
        ["Notificacoes:OutboxLembrete"] = "true",
        ["Notificacoes:OutboxOperacionais"] = "true",
        ["Notificacoes:OutboxRetorno"] = "true",
    };

    protected virtual void ConfigurarServicos(IServiceCollection servicos)
    {
    }

    public async Task InitializeAsync()
    {
        Fabrica = new PlataformaWebApplicationFactory(Postgres.ConnectionString, Configuracao, servicos =>
        {
            servicos.AddSingleton<TimeProvider>(Relogio);
            servicos.ConfigureDbContext<PlataformaDbContext>(o => o.AddInterceptors(Commit));
            ConfigurarServicos(servicos);
        });
        using var aquecimento = Fabrica.CreateClient();
        await Fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync()
    {
        await Fabrica.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    // ------------------------------------------------------------------ arranjo

    /// <summary>
    /// Negócio com administrador logado, profissional (e-mail e telefone), serviço, cliente (e-mail e telefone) e, por padrão, os dois
    /// canais ligados no negócio (WhatsApp ao cliente e aviso ao profissional por WhatsApp).
    /// </summary>
    protected async Task<CenarioOutbox> ArranjarAsync(bool whatsApp = true, bool avisoProfissionalPorWhatsApp = true, bool clienteComEmail = true)
    {
        var (admin, negocioId, _, _) = await Fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.ForcarAgendamento);
        var agenda = await Fabrica.CriarCenarioPadraoAsync(negocioId);

        var emailCliente = $"cliente-{Guid.NewGuid():N}@teste.com";
        var emailProfissional = $"prof-{Guid.NewGuid():N}@teste.com";
        const string TelefoneProfissional = "+5571988880000";

        await Fabrica.NoBancoAsync(async db =>
        {
            var negocio = await db.Negocios.IgnoreQueryFilters().SingleAsync(n => n.Id == negocioId);
            typeof(Plataforma.Dominio.Negocios.Negocio).GetProperty(nameof(negocio.WhatsAppAtivoParaConfirmacoes))!.SetValue(negocio, whatsApp);
            negocio.DefinirAvisoProfissionalPorWhatsApp(avisoProfissionalPorWhatsApp);

            var profissional = await db.Profissionais.IgnoreQueryFilters().SingleAsync(p => p.Id == agenda.ProfissionalId);
            profissional.AtualizarDados(profissional.Nome, TelefoneProfissional, emailProfissional, profissional.Endereco);

            if (clienteComEmail)
                (await db.Clientes.IgnoreQueryFilters().SingleAsync(c => c.Id == agenda.ClienteId)).PreencherEmailSeVazio(emailCliente);

            await db.SaveChangesAsync();
            return 0;
        });

        var segunda = SemeadorDeComissoes.Dia(3);
        while (segunda.DayOfWeek != DayOfWeek.Monday)
            segunda = segunda.AddDays(1);

        return new CenarioOutbox(
            admin, negocioId, agenda.ProfissionalId, agenda.ServicoId, agenda.ClienteId,
            SemeadorDeComissoes.Local(segunda, 0, 0), emailCliente, emailProfissional, TelefoneProfissional);
    }

    protected static DateTimeOffset Hora(CenarioOutbox c, int hora, int minuto = 0) =>
        c.Segunda.AddHours(hora).AddMinutes(minuto);

    // ------------------------------------------------------------------ serviço de agendamentos (como o controller o chamaria)

    protected async Task<T> ComServicoAsync<T>(Guid negocioId, Func<IServicoAgendamentos, Task<T>> acao)
    {
        using var escopo = Fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioId);
        return await acao(escopo.ServiceProvider.GetRequiredService<IServicoAgendamentos>());
    }

    protected async Task<Guid> CriarAgendamentoAsync(CenarioOutbox c, DateTimeOffset inicio)
    {
        var resultado = await ComServicoAsync(c.NegocioId, s => s.CriarAsync(
            new CriarAgendamento(c.ProfissionalId, c.ClienteId, [c.ServicoId], inicio, null)));
        Assert.True(resultado.Sucesso, resultado.MensagemErro);
        return resultado.AgendamentoId!.Value;
    }

    // ------------------------------------------------------------------ outbox

    protected async Task<int> DespacharAsync()
    {
        using var escopo = Fabrica.Services.CreateScope();
        return await escopo.ServiceProvider.GetRequiredService<JobDespacharNotificacoes>().ExecutarCicloAsync();
    }

    protected Task<List<NotificacaoPendente>> ItensAsync(Func<IQueryable<NotificacaoPendente>, IQueryable<NotificacaoPendente>>? filtro = null) =>
        Fabrica.NoBancoAsync(db =>
        {
            var consulta = db.NotificacoesPendentes.IgnoreQueryFilters().AsNoTracking();
            return (filtro?.Invoke(consulta) ?? consulta).OrderBy(n => n.CriadoEm).ThenBy(n => n.Canal).ToListAsync();
        });

    protected Task<List<NotificacaoPendente>> ItensDoAgendamentoAsync(Guid agendamentoId) =>
        ItensAsync(q => q.Where(n => n.AgendamentoId == agendamentoId));

    protected Task<int> SqlAsync(FormattableString sql) => Fabrica.NoBancoAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));
}
