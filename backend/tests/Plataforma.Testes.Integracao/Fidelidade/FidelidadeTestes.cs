using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Fidelidade;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Fidelidade;

/// <summary>Cartão de selos (seção 7, Sprint 5): configuração, selo automático ao concluir, resgate.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class FidelidadeTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public FidelidadeTestes(PostgresContainerFixture postgres) => _postgres = postgres;

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

    private async Task<Guid> CriarEConcluirAgendamentoAsync(HttpClient cliente, Guid profissionalId, Guid clienteId, Guid servicoId, TimeOnly hora)
    {
        var segunda = ProximaSegundaFeira();
        var criar = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            profissionalId, clienteId, [servicoId], AsDataHora(segunda, hora)));
        var agendamentoId = await criar.Content.ReadFromJsonAsync<Guid>();

        await cliente.PostAsync($"/painel/agendamentos/{agendamentoId}/concluir", null);
        return agendamentoId;
    }

    [Fact]
    public async Task Concluir_atendimentos_ate_o_limite_permite_resgatar()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.GerenciarFidelidade);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        (await cliente.PutAsJsonAsync("/painel/fidelidade", new DefinirProgramaFidelidade(2, "Corte grátis")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await CriarEConcluirAgendamentoAsync(cliente, cenario.ProfissionalId, cenario.ClienteId, cenario.ServicoId, new TimeOnly(9, 0));

        var progressoParcial = await cliente.GetFromJsonAsync<ProgressoFidelidade>($"/painel/fidelidade/clientes/{cenario.ClienteId}/progresso");
        progressoParcial!.SelosAtuais.Should().Be(1);
        progressoParcial.PodeResgatar.Should().BeFalse();

        await CriarEConcluirAgendamentoAsync(cliente, cenario.ProfissionalId, cenario.ClienteId, cenario.ServicoId, new TimeOnly(10, 0));

        var progressoCompleto = await cliente.GetFromJsonAsync<ProgressoFidelidade>($"/painel/fidelidade/clientes/{cenario.ClienteId}/progresso");
        progressoCompleto!.SelosAtuais.Should().Be(2);
        progressoCompleto.PodeResgatar.Should().BeTrue();

        var resgatar = await cliente.PostAsync($"/painel/fidelidade/clientes/{cenario.ClienteId}/resgatar", null);
        resgatar.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var progressoDepois = await cliente.GetFromJsonAsync<ProgressoFidelidade>($"/painel/fidelidade/clientes/{cenario.ClienteId}/progresso");
        progressoDepois!.SelosAtuais.Should().Be(0);
    }

    [Fact]
    public async Task Resgatar_sem_selos_suficientes_falha()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.GerenciarFidelidade);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        await cliente.PutAsJsonAsync("/painel/fidelidade", new DefinirProgramaFidelidade(5, "Corte grátis"));

        var resposta = await cliente.PostAsync($"/painel/fidelidade/clientes/{cenario.ClienteId}/resgatar", null);
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sem_programa_configurado_progresso_nao_conta_selos()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.GerenciarFidelidade);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        await CriarEConcluirAgendamentoAsync(cliente, cenario.ProfissionalId, cenario.ClienteId, cenario.ServicoId, new TimeOnly(9, 0));

        var progresso = await cliente.GetFromJsonAsync<ProgressoFidelidade>($"/painel/fidelidade/clientes/{cenario.ClienteId}/progresso");
        progresso!.PodeResgatar.Should().BeFalse();
        progresso.SelosNecessarios.Should().Be(0);
    }
}
