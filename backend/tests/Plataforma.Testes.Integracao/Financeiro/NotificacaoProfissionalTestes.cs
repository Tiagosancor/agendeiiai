using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Financeiro;

/// <summary>
/// E-mail ao profissional em novo/remarcado/cancelado (seção 9, Sprint 4, M: "e-mail ao
/// profissional em até 1 minuto após o agendamento").
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class NotificacaoProfissionalTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public NotificacaoProfissionalTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private static DateOnly ProximaSegundaFeira()
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var diasAteSegunda = ((int)DayOfWeek.Monday - (int)hoje.DayOfWeek + 7) % 7;
        return hoje.AddDays(diasAteSegunda == 0 ? 7 : diasAteSegunda);
    }

    private static DateTimeOffset AsDataHora(DateOnly dia, TimeOnly hora) =>
        new(dia.ToDateTime(hora, DateTimeKind.Unspecified), TimeSpan.FromHours(-3));

    private async Task DefinirEmailDoProfissionalAsync(Guid profissionalId, string email)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var profissional = await dbContext.Profissionais.IgnoreQueryFilters().FirstAsync(p => p.Id == profissionalId);
        profissional.AtualizarDados(profissional.Nome, profissional.Telefone, email, profissional.Endereco);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Criar_agendamento_no_painel_envia_email_de_novo_agendamento_ao_profissional()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        await DefinirEmailDoProfissionalAsync(cenario.ProfissionalId, "profissional@teste.com");

        var segunda = ProximaSegundaFeira();
        var resposta = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(10, 0))));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().ContainSingle(e => e.Destinatario == "profissional@teste.com" && e.Assunto.Contains("Novo agendamento"));
    }

    [Fact]
    public async Task Mover_agendamento_envia_email_de_remarcado_ao_profissional()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        await DefinirEmailDoProfissionalAsync(cenario.ProfissionalId, "profissional@teste.com");

        var segunda = ProximaSegundaFeira();
        var criar = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(10, 0))));
        var agendamentoId = await criar.Content.ReadFromJsonAsync<Guid>();

        var mover = await cliente.PutAsJsonAsync(
            $"/painel/agendamentos/{agendamentoId}/mover", new { NovoInicio = AsDataHora(segunda, new TimeOnly(11, 0)) });
        mover.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().Contain(e => e.Destinatario == "profissional@teste.com" && e.Assunto.Contains("remarcado"));
    }

    [Fact]
    public async Task Cancelar_agendamento_envia_email_de_cancelado_ao_profissional()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        await DefinirEmailDoProfissionalAsync(cenario.ProfissionalId, "profissional@teste.com");

        var segunda = ProximaSegundaFeira();
        var criar = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(10, 0))));
        var agendamentoId = await criar.Content.ReadFromJsonAsync<Guid>();

        var cancelar = await cliente.PostAsync($"/painel/agendamentos/{agendamentoId}/cancelar", null);
        cancelar.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().Contain(e => e.Destinatario == "profissional@teste.com" && e.Assunto.Contains("cancelado"));
    }

    [Fact]
    public async Task Profissional_sem_email_nao_gera_erro_nem_envio()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId); // sem e-mail definido

        var segunda = ProximaSegundaFeira();
        var resposta = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(10, 0))));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        var espiaEmail = _fabrica.Services.GetRequiredService<EspiaEmail>();
        espiaEmail.Enviados.Should().BeEmpty();
    }
}
