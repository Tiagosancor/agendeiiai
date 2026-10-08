using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Aplicacao.Fidelidade;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Agendamentos;

/// <summary>Atendimento sem agendamento — encaixe do balcão (seção 7, item 9 da seção 14).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class EncaixeTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public EncaixeTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Lancar_e_iniciar_cria_o_atendimento_ja_em_andamento_comecando_agora_com_a_duracao_dos_servicos()
    {
        var (admin, negocioId, usuarioId, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (profissionalId, servicos) = await CenarioDiaInteiroAsync(negocioId, duracoes: [15, 20]);
        // Turno 00:00–23:59: perto da meia-noite os 35 min atravessariam o dia (recusado de propósito).
        // Um fuso em que agora é perto do meio-dia deixa o teste independente da hora em que roda.
        await _fabrica.NoBancoAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE negocios SET fuso = {0} WHERE id = {1}", HardeningRelogioTestes.FusoPertoDoMeioDia(DateTimeOffset.UtcNow), negocioId));
        var antes = DateTimeOffset.UtcNow.AddMinutes(-1);

        var resposta = await LancarAsync(admin, new LancarEncaixe(profissionalId, null, new NovoClienteEncaixe("Walk-in", null),
            servicos, null, IniciarAtendimento: true, ClienteAutorizouMensagens: false));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        var agendamento = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters()
            .SingleAsync(a => a.Id == corpo.GetProperty("agendamentoId").GetGuid()));

        agendamento.Status.Should().Be(StatusAgendamento.EmAtendimento);
        agendamento.Origem.Should().Be(OrigemAgendamento.Encaixe);
        agendamento.Inicio.Should().BeOnOrAfter(antes).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        (agendamento.Fim - agendamento.Inicio).Should().Be(TimeSpan.FromMinutes(35));
        agendamento.MensagensAutorizadasEm.Should().BeNull();
        agendamento.PodeReceberMensagens.Should().BeFalse();
    }

    [Fact]
    public async Task Profissional_ocupado_devolve_409_com_os_proximos_horarios_livres()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (profissionalId, servicos) = await CenarioDiaInteiroAsync(negocioId, duracoes: [15]);
        var amanha9h = SemeadorDeComissoes.Local(SemeadorDeComissoes.Dia(1), 9, 0);

        (await LancarAsync(admin, Encaixar(profissionalId, servicos, amanha9h, "Primeiro"))).StatusCode.Should().Be(HttpStatusCode.Created);
        var porCima = await LancarAsync(admin, Encaixar(profissionalId, servicos, amanha9h, "Segundo"));

        porCima.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = await porCima.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("proximosHorariosLivres").GetArrayLength().Should().BeGreaterThan(0);
        corpo.GetProperty("clienteId").GetGuid().Should().NotBeEmpty(); // o cadastro rápido já feito volta para a tela reusar
    }

    [Fact]
    public async Task Cliente_sem_telefone_e_aceito_sem_quebrar_o_indice_unico_e_telefone_repetido_continua_recusado()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (profissionalId, servicos) = await CenarioDiaInteiroAsync(negocioId, duracoes: [15]);
        var dia = SemeadorDeComissoes.Dia(1);

        // Dois clientes sem telefone convivem (NULL não conflita no índice único).
        (await LancarAsync(admin, Encaixar(profissionalId, servicos, SemeadorDeComissoes.Local(dia, 9, 0), "Sem Telefone Um")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await LancarAsync(admin, Encaixar(profissionalId, servicos, SemeadorDeComissoes.Local(dia, 10, 0), "Sem Telefone Dois")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters().CountAsync(c => c.NegocioId == negocioId && c.Telefone == null)))
            .Should().Be(2);

        // Cadastro comum pelo painel: telefone opcional, mas repetido continua recusado.
        (await admin.PostAsJsonAsync("/painel/clientes", new CriarCliente("Sem Telefone Três", null))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync("/painel/clientes", new CriarCliente("Ana", "(71) 98888-7777"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync("/painel/clientes", new CriarCliente("Outra Ana", "71988887777"))).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // No balcão, o mesmo telefone reaproveita o cliente em vez de duplicar.
        var comTelefone = await LancarAsync(admin, new LancarEncaixe(profissionalId, null, new NovoClienteEncaixe("Ana de novo", "+5571988887777"),
            servicos, SemeadorDeComissoes.Local(dia, 11, 0), false, false));
        comTelefone.StatusCode.Should().Be(HttpStatusCode.Created);
        (await _fabrica.NoBancoAsync(db => db.Clientes.IgnoreQueryFilters()
            .CountAsync(c => c.NegocioId == negocioId && c.Telefone == TelefoneE164.Criar("+5571988887777")))).Should().Be(1);

        var busca = await admin.GetFromJsonAsync<List<ClienteResumo>>("/painel/encaixes/clientes?busca=8888-77");
        busca!.Should().ContainSingle(c => c.Nome == "Ana");
        (await admin.GetFromJsonAsync<List<ClienteResumo>>("/painel/encaixes/clientes?busca=telefone"))!.Should().HaveCount(3);
    }

    [Fact]
    public async Task Sem_a_permissao_recebe_403_e_a_Recepcionista_tem_por_padrao()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (profissionalId, servicos) = await CenarioDiaInteiroAsync(negocioId, duracoes: [15]);
        var dados = Encaixar(profissionalId, servicos, SemeadorDeComissoes.Local(SemeadorDeComissoes.Dia(1), 9, 0), "Fulano");

        using var profissional = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, profissionalId);
        (await LancarAsync(profissional, dados)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await profissional.GetAsync("/painel/encaixes/opcoes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista,
            permissoesExtras: Usuario.PermissoesPadrao(Perfil.Recepcionista).ToArray());
        (await LancarAsync(recepcao, dados)).StatusCode.Should().Be(HttpStatusCode.Created);
        var opcoes = await recepcao.GetFromJsonAsync<JsonElement>("/painel/encaixes/opcoes");
        opcoes.GetProperty("profissionais").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Sem_o_aceite_nenhuma_confirmacao_nem_lembrete_sai_para_o_cliente()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (profissionalId, servicos) = await CenarioDiaInteiroAsync(negocioId, duracoes: [15]);
        var semAceite = await ClienteComEmailAsync(admin, "sem-aceite@teste.com");
        var comAceite = await ClienteComEmailAsync(admin, "com-aceite@teste.com");
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();

        // Daqui a 40 e 45 min: dentro da janela do lembrete (60 min).
        var inicio = DateTimeOffset.UtcNow.AddMinutes(40);
        var idSem = await IdAsync(await LancarAsync(admin, new LancarEncaixe(profissionalId, semAceite, null, servicos, inicio, false, false)));
        var idCom = await IdAsync(await LancarAsync(admin, new LancarEncaixe(profissionalId, comAceite, null, servicos, inicio.AddMinutes(20), false, true)));

        espia.Enviados.Should().NotContain(e => e.Destinatario == "sem-aceite@teste.com");
        espia.Enviados.Should().ContainSingle(e => e.Destinatario == "com-aceite@teste.com" && e.Assunto.Contains("confirmado"));

        // Marcados "ontem", para o lembrete fazer sentido; o job decide só pelo aceite.
        await _fabrica.NoBancoAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE agendamentos SET horario_combinado_em = now() - interval '1 day' WHERE id IN ({0}, {1})", idSem, idCom));
        using (var escopo = _fabrica.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<JobEnviarLembretes>().ExecutarAsync();

        espia.Enviados.Should().NotContain(e => e.Destinatario == "sem-aceite@teste.com");
        espia.Enviados.Should().ContainSingle(e => e.Destinatario == "com-aceite@teste.com" && e.Assunto.Contains("Lembrete"));

        var autorizado = await _fabrica.NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().SingleAsync(a => a.Id == idCom));
        autorizado.MensagensAutorizadasEm.Should().NotBeNull();
        autorizado.MensagensAutorizadasPorUsuarioId.Should().NotBeNull();
    }

    [Fact]
    public async Task Encaixe_gera_pagamento_comissao_e_selo_normalmente()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (profissionalId, servicos) = await CenarioDiaInteiroAsync(negocioId, duracoes: [15]);
        await admin.DefinirComissaoAsync(profissionalId, 20m);
        await admin.PutAsJsonAsync("/painel/fidelidade", new DefinirProgramaFidelidade(5, "Corte grátis"));

        var id = await IdAsync(await LancarAsync(admin, Encaixar(profissionalId, servicos, SemeadorDeComissoes.Local(SemeadorDeComissoes.Dia(1), 10, 0), "Balcão")));
        (await admin.PostAsync($"/painel/agendamentos/{id}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync("/painel/pagamentos", new RegistrarPagamento(id, 40m, "Dinheiro"))).StatusCode.Should().Be(HttpStatusCode.Created);

        var linha = await _fabrica.NoBancoAsync(db => db.Set<AgendamentoServico>().IgnoreQueryFilters().SingleAsync(s => s.AgendamentoId == id));
        linha.ComissaoValor.Should().Be(8m); // 20% de R$ 40,00
        (await _fabrica.NoBancoAsync(db => db.SelosCliente.IgnoreQueryFilters().AnyAsync(s => s.AgendamentoId == id))).Should().BeTrue();
        (await _fabrica.NoBancoAsync(db => db.Pagamentos.IgnoreQueryFilters().AnyAsync(p => p.AgendamentoId == id))).Should().BeTrue();
    }

    // ---------------------------------------------------------------- apoio

    /// <summary>Profissional com expediente de 00:00 a 23:59 todos os dias (o encaixe respeita o expediente) e serviços de R$ 40,00.</summary>
    private Task<(Guid ProfissionalId, List<Guid> Servicos)> CenarioDiaInteiroAsync(Guid negocioId, int[] duracoes) =>
        _fabrica.NoBancoAsync(async db =>
        {
            var profissional = Profissional.Criar(negocioId, "Profissional do Balcão");
            db.Profissionais.Add(profissional);
            foreach (var dia in Enum.GetValues<DiaSemana>())
                db.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocioId, profissional.Id, dia, new TimeOnly(0, 0), new TimeOnly(23, 59)));

            var categoria = Categoria.Criar(negocioId, "Cabelo");
            db.Categorias.Add(categoria);
            var servicos = duracoes.Select((d, i) => Servico.Criar(negocioId, categoria.Id, $"Serviço {i + 1}", 40m, d)).ToList();
            db.Servicos.AddRange(servicos);
            await db.SaveChangesAsync();
            return (profissional.Id, servicos.Select(s => s.Id).ToList());
        });

    private static LancarEncaixe Encaixar(Guid profissionalId, List<Guid> servicos, DateTimeOffset inicio, string nomeCliente) =>
        new(profissionalId, null, new NovoClienteEncaixe(nomeCliente, null), servicos, inicio, IniciarAtendimento: false, ClienteAutorizouMensagens: false);

    private static Task<HttpResponseMessage> LancarAsync(HttpClient cliente, LancarEncaixe dados) =>
        cliente.PostAsJsonAsync("/painel/encaixes", dados);

    private static async Task<Guid> IdAsync(HttpResponseMessage resposta)
    {
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("agendamentoId").GetGuid();
    }

    private static async Task<Guid> ClienteComEmailAsync(HttpClient admin, string email)
    {
        var resposta = await admin.PostAsJsonAsync("/painel/clientes",
            new CriarCliente("Cliente " + email, $"719{Random.Shared.Next(10000000, 99999999)}", email));
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }
}
