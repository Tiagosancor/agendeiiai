using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Clientes;

[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class LembreteRetornoApiTestes : IAsyncLifetime
{
    private const string RotaConfiguracao = "/painel/configuracoes/lembrete-retorno";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public LembreteRetornoApiTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Negocio_novo_comeca_desligado_com_os_padroes()
    {
        var (http, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var configuracao = await http.GetFromJsonAsync<ConfiguracaoLembreteRetorno>(RotaConfiguracao);

        configuracao.Should().Be(new ConfiguracaoLembreteRetorno(false, 30, "10:00", LembreteRetorno.MensagemPadrao));
    }

    [Fact]
    public async Task Salvar_liga_grava_e_desligar_preserva_a_configuracao()
    {
        var (http, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var ligar = await http.PutAsJsonAsync(RotaConfiguracao,
            new { ativo = true, dias = 45, horario = "09:30", mensagem = "Oi {nome_cliente}, volte à {nome_empresa}: {link_agendamento}" });
        ligar.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var ligadoEm = (await NegocioAsync(negocioId)).LembreteRetornoAtivadoEm;
        ligadoEm.Should().NotBeNull();

        var desligar = await http.PutAsJsonAsync(RotaConfiguracao,
            new { ativo = false, dias = 45, horario = "09:30", mensagem = "Oi {nome_cliente}, volte à {nome_empresa}: {link_agendamento}" });
        desligar.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await http.GetFromJsonAsync<ConfiguracaoLembreteRetorno>(RotaConfiguracao)).Should().Be(
            new ConfiguracaoLembreteRetorno(false, 45, "09:30", "Oi {nome_cliente}, volte à {nome_empresa}: {link_agendamento}"));

        // Religar marca um momento novo (nada retroativo).
        await http.PutAsJsonAsync(RotaConfiguracao,
            new { ativo = true, dias = 45, horario = "09:30", mensagem = "Oi {nome_cliente}" });
        (await NegocioAsync(negocioId)).LembreteRetornoAtivadoEm.Should().BeAfter(ligadoEm!.Value);
    }

    public static TheoryData<object, string> Invalidos => new()
    {
        { new { ativo = true, dias = 0, horario = "10:00", mensagem = "Oi" }, "dias" },
        { new { ativo = true, dias = -5, horario = "10:00", mensagem = "Oi" }, "dias" },
        { new { ativo = true, dias = 30, horario = "25:00", mensagem = "Oi" }, "horario" },
        { new { ativo = true, dias = 30, horario = "10h", mensagem = "Oi" }, "horario" },
        { new { ativo = true, dias = 30, horario = "", mensagem = "Oi" }, "horario" },
        { new { ativo = true, dias = 30, horario = "10:00", mensagem = "" }, "mensagem" },
        { new { ativo = true, dias = 30, horario = "10:00", mensagem = "   " }, "mensagem" },
        { new { ativo = true, dias = 30, horario = "10:00", mensagem = "Oi {nome}, volte!" }, "mensagem" },
        { new { dias = 30, horario = "10:00", mensagem = "Oi" }, "ativo" },
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public async Task Configuracao_invalida_da_400_com_o_campo_e_nao_grava(object corpo, string campo)
    {
        var (http, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var resposta = await http.PutAsJsonAsync(RotaConfiguracao, corpo);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errors").TryGetProperty(campo, out _).Should().BeTrue();
        (await NegocioAsync(negocioId)).LembreteRetornoAtivo.Should().BeFalse();
    }

    [Fact]
    public async Task Varios_erros_voltam_juntos_por_campo()
    {
        var (http, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var resposta = await http.PutAsJsonAsync(RotaConfiguracao, new { ativo = true, dias = 0, horario = "x", mensagem = "{oi}" });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("dias", "horario", "mensagem");
    }

    [Fact]
    public async Task Sem_a_permissao_de_configurar_o_negocio_da_403_e_sem_login_401()
    {
        var (profissional, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Profissional);
        using var anonimo = _fabrica.CreateClient();

        (await profissional.GetAsync(RotaConfiguracao)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await profissional.PutAsJsonAsync(RotaConfiguracao, new { ativo = true, dias = 30, horario = "10:00", mensagem = "Oi" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await profissional.GetAsync("/painel/lembrete-retorno/relatorio")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await profissional.PatchAsJsonAsync($"/painel/clientes/{Guid.NewGuid()}/aceita-lembrete-retorno", new { aceita = false }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await anonimo.GetAsync(RotaConfiguracao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Aceite_do_cliente_muda_so_no_proprio_negocio()
    {
        var (http, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (_, outroNegocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var meu = await CriarClienteAsync(negocioId);
        var deOutro = await CriarClienteAsync(outroNegocioId);

        (await http.PatchAsJsonAsync($"/painel/clientes/{meu}/aceita-lembrete-retorno", new { aceita = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await http.PatchAsJsonAsync($"/painel/clientes/{deOutro}/aceita-lembrete-retorno", new { aceita = false }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await http.PatchAsJsonAsync($"/painel/clientes/{meu}/aceita-lembrete-retorno", new { }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await ClienteAsync(meu)).AceitaLembreteRetorno.Should().BeFalse();
        (await ClienteAsync(deOutro)).AceitaLembreteRetorno.Should().BeTrue();
    }

    [Fact]
    public async Task Configurar_um_negocio_nao_mexe_no_outro()
    {
        var (http, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (outro, outroNegocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        await http.PutAsJsonAsync(RotaConfiguracao, new { ativo = true, dias = 15, horario = "08:00", mensagem = "Oi" });

        (await outro.GetFromJsonAsync<ConfiguracaoLembreteRetorno>(RotaConfiguracao))!.Ativo.Should().BeFalse();
        (await NegocioAsync(outroNegocioId)).LembreteRetornoDias.Should().Be(30);
    }

    [Fact]
    public async Task Relatorio_conta_so_os_envios_do_proprio_negocio_no_periodo()
    {
        var (http, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (_, outroNegocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        await CriarEnvioAsync(negocioId, StatusLembreteRetorno.Enviado, DateTimeOffset.UtcNow.AddDays(-2), gerouAgendamento: true);
        await CriarEnvioAsync(negocioId, StatusLembreteRetorno.Enviado, DateTimeOffset.UtcNow.AddDays(-5), gerouAgendamento: false);
        await CriarEnvioAsync(negocioId, StatusLembreteRetorno.Falhou, DateTimeOffset.UtcNow.AddDays(-1), gerouAgendamento: false);
        await CriarEnvioAsync(negocioId, StatusLembreteRetorno.Enviado, DateTimeOffset.UtcNow.AddDays(-40), gerouAgendamento: true);
        await CriarEnvioAsync(outroNegocioId, StatusLembreteRetorno.Enviado, DateTimeOffset.UtcNow.AddDays(-1), gerouAgendamento: true);

        (await http.GetFromJsonAsync<RelatorioLembreteRetorno>("/painel/lembrete-retorno/relatorio?dias=30"))
            .Should().Be(new RelatorioLembreteRetorno(30, 2, 1));
        (await http.GetFromJsonAsync<RelatorioLembreteRetorno>("/painel/lembrete-retorno/relatorio"))
            .Should().Be(new RelatorioLembreteRetorno(30, 2, 1));
        (await http.GetFromJsonAsync<RelatorioLembreteRetorno>("/painel/lembrete-retorno/relatorio?dias=60"))
            .Should().Be(new RelatorioLembreteRetorno(60, 3, 2));
        (await http.GetAsync("/painel/lembrete-retorno/relatorio?dias=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Lista_de_clientes_traz_o_aceite()
    {
        var (http, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cliente = await CriarClienteAsync(negocioId, aceita: false);

        var lista = await http.GetFromJsonAsync<List<ClienteResumo>>("/painel/clientes");

        lista.Should().ContainSingle(c => c.Id == cliente).Which.AceitaLembreteRetorno.Should().BeFalse();
    }

    // ---------- arranjo ----------

    private async Task<Guid> CriarClienteAsync(Guid negocioId, bool aceita = true)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var cliente = Cliente.Criar(negocioId, "Cliente", TelefoneE164.Criar($"+55719{Random.Shared.Next(10000000, 99999999)}"));
        cliente.DefinirAceitaLembreteRetorno(aceita);
        dbContext.Clientes.Add(cliente);
        await dbContext.SaveChangesAsync();
        return cliente.Id;
    }

    private async Task CriarEnvioAsync(Guid negocioId, StatusLembreteRetorno status, DateTimeOffset enviadoEm, bool gerouAgendamento)
    {
        var clienteId = await CriarClienteAsync(negocioId);

        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var servicos = new[] { new Plataforma.Dominio.Agendamentos.ItemServicoAgendamento(Guid.NewGuid(), "Corte", 50m, 30) };
        var atendimento = Plataforma.Dominio.Agendamentos.Agendamento.CriarConfirmado(
            negocioId, Guid.NewGuid(), clienteId, enviadoEm.AddDays(-30), servicos);
        dbContext.Agendamentos.Add(atendimento);

        var envio = LembreteRetornoEnvio.Iniciar(negocioId, clienteId, atendimento.Id, DateOnly.FromDateTime(enviadoEm.UtcDateTime), enviadoEm);
        if (status == StatusLembreteRetorno.Enviado)
            envio.MarcarEnviado(null, enviadoEm);
        else
            envio.MarcarFalha("fora do ar", enviadoEm);
        if (gerouAgendamento)
            envio.MarcarAgendamentoGerado(atendimento.Id);

        dbContext.LembretesRetornoEnvios.Add(envio);
        await dbContext.SaveChangesAsync();
    }

    private async Task<Negocio> NegocioAsync(Guid negocioId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        return await dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == negocioId);
    }

    private async Task<Cliente> ClienteAsync(Guid clienteId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        return await dbContext.Clientes.IgnoreQueryFilters().AsNoTracking().FirstAsync(c => c.Id == clienteId);
    }
}
