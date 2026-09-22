using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Financeiro;

/// <summary>Registro de pagamento e faturamento por período/profissional/serviço (seção 7, Sprint 4).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class FinanceiroTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public FinanceiroTestes(PostgresContainerFixture postgres) => _postgres = postgres;

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
    public async Task Registrar_pagamento_e_ver_no_resumo_do_periodo()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.VerFinanceiro);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        var segunda = ProximaSegundaFeira();
        var criar = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(10, 0))));
        var agendamentoId = await criar.Content.ReadFromJsonAsync<Guid>();

        var registrar = await cliente.PostAsJsonAsync(
            "/painel/pagamentos", new RegistrarPagamento(agendamentoId, 50m, "Pix"));
        registrar.StatusCode.Should().Be(HttpStatusCode.Created);

        var inicio = segunda.AddDays(-1);
        var fim = segunda.AddDays(1);
        var resumo = await cliente.GetFromJsonAsync<ResumoFinanceiro>($"/painel/financeiro/resumo?inicio={inicio:yyyy-MM-dd}&fim={fim:yyyy-MM-dd}");

        resumo!.Total.Should().Be(50m);
        resumo.QuantidadeAtendimentos.Should().Be(1);
        resumo.PorProfissional.Should().ContainSingle(p => p.ProfissionalId == cenario.ProfissionalId && p.Total == 50m);
        resumo.PorServico.Should().ContainSingle(s => s.ServicoId == cenario.ServicoId && s.Total == 50m);
    }

    [Fact]
    public async Task Registrar_pagamento_duas_vezes_para_o_mesmo_agendamento_falha()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.VerFinanceiro);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        var segunda = ProximaSegundaFeira();
        var criar = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(10, 0))));
        var agendamentoId = await criar.Content.ReadFromJsonAsync<Guid>();

        (await cliente.PostAsJsonAsync("/painel/pagamentos", new RegistrarPagamento(agendamentoId, 50m, "Pix")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var segundaTentativa = await cliente.PostAsJsonAsync("/painel/pagamentos", new RegistrarPagamento(agendamentoId, 50m, "Dinheiro"));
        segundaTentativa.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Resumo_fora_do_periodo_nao_conta_o_pagamento()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador, Permissao.VerFinanceiro);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        var segunda = ProximaSegundaFeira();
        var criar = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(10, 0))));
        var agendamentoId = await criar.Content.ReadFromJsonAsync<Guid>();
        await cliente.PostAsJsonAsync("/painel/pagamentos", new RegistrarPagamento(agendamentoId, 50m, "Pix"));

        // Período bem depois do agendamento — não deve aparecer.
        var inicio = segunda.AddDays(30);
        var fim = segunda.AddDays(31);
        var resumo = await cliente.GetFromJsonAsync<ResumoFinanceiro>($"/painel/financeiro/resumo?inicio={inicio:yyyy-MM-dd}&fim={fim:yyyy-MM-dd}");

        resumo!.Total.Should().Be(0m);
        resumo.QuantidadeAtendimentos.Should().Be(0);
    }
}
