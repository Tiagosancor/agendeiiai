using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Fidelidade;

/// <summary>Exportação e exclusão sob demanda (LGPD, seção 8.4, Sprint 5).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class LgpdClienteTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public LgpdClienteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

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

    [Fact]
    public async Task Exportar_dados_do_cliente_traz_cadastro_e_historico()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.GerenciarClientes);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        var segunda = ProximaSegundaFeira();
        await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(10, 0))));

        var exportacao = await cliente.GetFromJsonAsync<ExportacaoCliente>($"/painel/clientes/{cenario.ClienteId}/exportar");

        exportacao!.Id.Should().Be(cenario.ClienteId);
        exportacao.Agendamentos.Should().ContainSingle();
    }

    [Fact]
    public async Task Excluir_cliente_anonimiza_sem_apagar_o_historico()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.GerenciarClientes);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        var segunda = ProximaSegundaFeira();
        await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(10, 0))));

        var excluir = await cliente.PostAsync($"/painel/clientes/{cenario.ClienteId}/excluir", null);
        excluir.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var clienteAtualizado = await cliente.GetFromJsonAsync<ClienteResumo>($"/painel/clientes/{cenario.ClienteId}");
        clienteAtualizado!.Nome.Should().Be("Cliente removido");
        clienteAtualizado.Email.Should().BeNull();
        clienteAtualizado.Excluido.Should().BeTrue();

        // Histórico continua existindo (auditoria/financeiro) — só não identifica mais ninguém.
        var exportacao = await cliente.GetFromJsonAsync<ExportacaoCliente>($"/painel/clientes/{cenario.ClienteId}/exportar");
        exportacao!.Agendamentos.Should().ContainSingle();
    }

    [Fact]
    public async Task Excluir_cliente_inexistente_da_404()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.GerenciarClientes);

        var resposta = await cliente.PostAsync($"/painel/clientes/{Guid.NewGuid()}/excluir", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
