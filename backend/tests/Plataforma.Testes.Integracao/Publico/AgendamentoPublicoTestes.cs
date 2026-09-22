using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Controllers.Publico;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Publico;

/// <summary>
/// Assistente público de agendamento (seção 6.2/8.1/8.2 — Sprint 3). Testes obrigatórios
/// da seção 8.1: agendar sem token, token de outro telefone/negócio, identificação
/// automática do cliente sem sobrescrever dados, concorrência na criação de cliente com o
/// mesmo telefone.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AgendamentoPublicoTestes : IAsyncLifetime
{
    private const string DominioBase = DominioDeTeste.Valor;

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public AgendamentoPublicoTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Confirmar_sem_token_falha()
    {
        var (slug, _, profissionalId, servicoId) = await SemearNegocioComAgendaAsync();
        using var cliente = ClientePara(slug);

        var agendamentoId = await CriarReservaAsync(cliente, profissionalId, servicoId);

        var resposta = await cliente.PostAsJsonAsync("/publico/agendamentos", new AgendamentosPublicoController.ConfirmarAgendamentoRequisicao(
            agendamentoId, "", "Cliente Teste", TelefoneAleatorio(), null, null, null));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirmar_com_token_de_outro_telefone_falha()
    {
        var (slug, _, profissionalId, servicoId) = await SemearNegocioComAgendaAsync();
        using var cliente = ClientePara(slug);

        var agendamentoId = await CriarReservaAsync(cliente, profissionalId, servicoId);
        var telefoneDoToken = TelefoneAleatorio();
        var token = await SolicitarEValidarCodigoAsync(cliente, telefoneDoToken);

        var resposta = await cliente.PostAsJsonAsync("/publico/agendamentos", new AgendamentosPublicoController.ConfirmarAgendamentoRequisicao(
            agendamentoId, token, "Cliente Teste", TelefoneAleatorio(), null, null, null)); // telefone diferente do token

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirmar_com_token_de_outro_negocio_falha()
    {
        var (slugA, _, profissionalIdA, servicoIdA) = await SemearNegocioComAgendaAsync();
        var (slugB, _, _, _) = await SemearNegocioComAgendaAsync();

        using var clienteA = ClientePara(slugA);
        using var clienteB = ClientePara(slugB);

        var agendamentoIdA = await CriarReservaAsync(clienteA, profissionalIdA, servicoIdA);
        var telefone = TelefoneAleatorio();

        // Token de verificação emitido para o negócio B...
        var tokenDoNegocioB = await SolicitarEValidarCodigoAsync(clienteB, telefone);

        // ...usado para confirmar um agendamento no negócio A. O host da chamada é sempre A
        // (é assim que o middleware resolve o tenant), então o token de B nunca bate.
        var resposta = await clienteA.PostAsJsonAsync("/publico/agendamentos", new AgendamentosPublicoController.ConfirmarAgendamentoRequisicao(
            agendamentoIdA, tokenDoNegocioB, "Cliente Teste", telefone, null, null, null));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Fluxo_completo_confirma_agendamento_e_cria_cliente_pelo_telefone()
    {
        var (slug, negocioId, profissionalId, servicoId) = await SemearNegocioComAgendaAsync();
        using var cliente = ClientePara(slug);
        var telefone = TelefoneAleatorio();

        var agendamentoId = await CriarReservaAsync(cliente, profissionalId, servicoId);
        var token = await SolicitarEValidarCodigoAsync(cliente, telefone);

        var resposta = await cliente.PostAsJsonAsync("/publico/agendamentos", new AgendamentosPublicoController.ConfirmarAgendamentoRequisicao(
            agendamentoId, token, "Fulano da Silva", telefone, "fulano@teste.com", "Sem barba, só cabelo", null));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var clienteCriado = await dbContext.Clientes.IgnoreQueryFilters()
            .FirstAsync(c => c.NegocioId == negocioId && c.Telefone == TelefoneE164.Criar(telefone));

        clienteCriado.Nome.Should().Be("Fulano da Silva");
        clienteCriado.Email.Should().Be("fulano@teste.com");
        clienteCriado.Origem.Should().Be(OrigemCliente.LinkPublico);
    }

    [Fact]
    public async Task Identificacao_automatica_nao_sobrescreve_dados_do_cliente_existente()
    {
        var (slug, negocioId, profissionalId, servicoId) = await SemearNegocioComAgendaAsync();
        var telefone = TelefoneAleatorio();

        using (var escopo = _fabrica.Services.CreateScope())
        {
            var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            dbContext.Clientes.Add(Cliente.Criar(
                negocioId, "Nome Original", TelefoneE164.Criar(telefone), OrigemCliente.Painel, "email-original@teste.com"));
            await dbContext.SaveChangesAsync();
        }

        using var cliente = ClientePara(slug);
        var agendamentoId = await CriarReservaAsync(cliente, profissionalId, servicoId);
        var token = await SolicitarEValidarCodigoAsync(cliente, telefone);

        // Nome e e-mail diferentes do cadastro — não pode sobrescrever (seção 8.1.4.a).
        var resposta = await cliente.PostAsJsonAsync("/publico/agendamentos", new AgendamentosPublicoController.ConfirmarAgendamentoRequisicao(
            agendamentoId, token, "Nome Diferente", telefone, "outro-email@teste.com", null, null));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);

        using var escopoVerificacao = _fabrica.Services.CreateScope();
        var dbContextVerificacao = escopoVerificacao.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var clienteAtualizado = await dbContextVerificacao.Clientes.IgnoreQueryFilters()
            .FirstAsync(c => c.NegocioId == negocioId && c.Telefone == TelefoneE164.Criar(telefone));
        clienteAtualizado.Nome.Should().Be("Nome Original");
        clienteAtualizado.Email.Should().Be("email-original@teste.com");

        var totalDeClientes = await dbContextVerificacao.Clientes.IgnoreQueryFilters()
            .CountAsync(c => c.NegocioId == negocioId && c.Telefone == TelefoneE164.Criar(telefone));
        totalDeClientes.Should().Be(1);

        var agendamento = await dbContextVerificacao.Agendamentos.IgnoreQueryFilters().FirstAsync(a => a.Id == agendamentoId);
        agendamento.NomeInformado.Should().Be("Nome Diferente");
    }

    [Fact]
    public async Task Duas_confirmacoes_simultaneas_com_o_mesmo_telefone_criam_so_um_cliente()
    {
        var (slug, negocioId, profissionalId, servicoId) = await SemearNegocioComAgendaAsync();
        using var cliente = ClientePara(slug);
        var telefone = TelefoneAleatorio();

        var segunda = ProximaSegundaFeira();
        var agendamentoId1 = await CriarReservaAsync(cliente, profissionalId, servicoId, AsDataHora(segunda, new TimeOnly(10, 0)));
        var agendamentoId2 = await CriarReservaAsync(cliente, profissionalId, servicoId, AsDataHora(segunda, new TimeOnly(11, 0)));

        var token1 = await SolicitarEValidarCodigoAsync(cliente, telefone);
        var token2 = await SolicitarEValidarCodigoAsync(cliente, telefone);

        var tarefa1 = cliente.PostAsJsonAsync("/publico/agendamentos", new AgendamentosPublicoController.ConfirmarAgendamentoRequisicao(
            agendamentoId1, token1, "Cliente Concorrente", telefone, null, null, null));
        var tarefa2 = cliente.PostAsJsonAsync("/publico/agendamentos", new AgendamentosPublicoController.ConfirmarAgendamentoRequisicao(
            agendamentoId2, token2, "Cliente Concorrente", telefone, null, null, null));

        var respostas = await Task.WhenAll(tarefa1, tarefa2);
        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var totalDeClientes = await dbContext.Clientes.IgnoreQueryFilters()
            .CountAsync(c => c.NegocioId == negocioId && c.Telefone == TelefoneE164.Criar(telefone));

        totalDeClientes.Should().Be(1);
    }

    [Fact]
    public async Task Nenhum_endpoint_publico_de_catalogo_devolve_campo_pessoal_de_cliente()
    {
        var (slug, _, _, _) = await SemearNegocioComAgendaAsync();
        using var cliente = ClientePara(slug);

        var servicos = await cliente.GetStringAsync("/publico/servicos");
        var profissionais = await cliente.GetStringAsync("/publico/profissionais");

        // Nenhum dos DTOs públicos tem campo de telefone/e-mail/CPF de cliente algum — a
        // checagem estrutural (os tipos ServicoPublico/ProfissionalPublico) já garante isso
        // em tempo de compilação; aqui só provamos que a resposta HTTP de verdade não
        // extrapola esses tipos.
        servicos.Should().NotContain("telefone").And.NotContain("cpf");
        profissionais.Should().NotContain("telefone").And.NotContain("cpf");
    }

    private async Task<Guid> CriarReservaAsync(
        HttpClient cliente, Guid profissionalId, Guid servicoId, DateTimeOffset? inicio = null)
    {
        var resposta = await cliente.PostAsJsonAsync("/publico/reservas", new AgendamentosPublicoController.CriarReservaRequisicao(
            profissionalId, [servicoId], inicio ?? AsDataHora(ProximaSegundaFeira(), new TimeOnly(10, 0))));

        resposta.EnsureSuccessStatusCode();
        var corpo = await resposta.Content.ReadFromJsonAsync<Dictionary<string, Guid>>();
        return corpo!["agendamentoId"];
    }

    private async Task<string> SolicitarEValidarCodigoAsync(HttpClient cliente, string telefone)
    {
        await cliente.PostAsJsonAsync("/publico/codigos", new CodigosPublicoController.SolicitarCodigoRequisicao(telefone, null));

        var espiaWhatsApp = _fabrica.Services.GetRequiredService<EspiaWhatsApp>();
        var mensagem = espiaWhatsApp.Enviados.Last(e => e.Telefone.Valor == telefone).Mensagem;
        var codigo = Regex.Match(mensagem, @"\b\d{6}\b").Value;

        var respostaValidar = await cliente.PostAsJsonAsync("/publico/codigos/validar", new CodigosPublicoController.ValidarCodigoRequisicao(telefone, codigo));
        var corpo = await respostaValidar.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return corpo!["tokenVerificacao"];
    }

    private async Task<(string Slug, Guid NegocioId, Guid ProfissionalId, Guid ServicoId)> SemearNegocioComAgendaAsync()
    {
        using var escopo = _fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var slug = "pub-" + Guid.NewGuid().ToString("N")[..12];
        var negocio = Negocio.Criar(Slug.Criar(slug), "Negócio Público de Teste", TipoNegocio.Barbearia);
        dbContext.Negocios.Add(negocio);
        await dbContext.SaveChangesAsync();

        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocio.Id);
        return (slug, negocio.Id, cenario.ProfissionalId, cenario.ServicoId);
    }

    private static DateOnly ProximaSegundaFeira()
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var diasAteSegunda = ((int)DayOfWeek.Monday - (int)hoje.DayOfWeek + 7) % 7;
        return hoje.AddDays(diasAteSegunda == 0 ? 7 : diasAteSegunda);
    }

    private static DateTimeOffset AsDataHora(DateOnly dia, TimeOnly hora) =>
        new(dia.ToDateTime(hora, DateTimeKind.Unspecified), TimeSpan.FromHours(-3));

    private static string TelefoneAleatorio() => $"+55719{Random.Shared.Next(10000000, 99999999)}";

    private HttpClient ClientePara(string slug) => _fabrica.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri($"http://{slug}.{DominioBase}/"),
    });
}
