using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Cadastros;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Cadastros;

/// <summary>Editar e excluir usuários, profissionais e serviços (seção 7 — ajuste 5 da seção 14, testes obrigatórios).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class EditarExcluirCadastrosTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public EditarExcluirCadastrosTestes(PostgresContainerFixture postgres) => _postgres = postgres;

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

    private async Task<T> NoBancoAsync<T>(Func<PlataformaDbContext, Task<T>> consulta)
    {
        using var escopo = _fabrica.Services.CreateScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>());
    }

    private static async Task<Guid> AgendarAsync(HttpClient cliente, SemeadorDeAgenda.CenarioDeAgenda cenario, DateTimeOffset inicio)
    {
        var resposta = await cliente.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], inicio));
        resposta.StatusCode.Should().Be(HttpStatusCode.Created, await resposta.Content.ReadAsStringAsync());
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    /// <summary>Login de um usuário criado dentro do negócio (mesmo tenant) — devolve cliente autenticado.</summary>
    private async Task<HttpClient> LogarAsync(string email, string senha)
    {
        using var anonimo = _fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, senha));
        login.EnsureSuccessStatusCode();
        var corpo = await login.Content.ReadFromJsonAsync<RespostaLogin>();

        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", corpo!.AccessToken);
        return cliente;
    }

    private static async Task<Guid> CriarUsuarioAsync(HttpClient admin, string email, Perfil perfil, string senha = "SenhaForte!123")
    {
        var resposta = await admin.PostAsJsonAsync("/painel/usuarios", new CriarUsuario("Pessoa " + perfil, email, senha, perfil));
        resposta.StatusCode.Should().Be(HttpStatusCode.Created, await resposta.Content.ReadAsStringAsync());
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    [Fact]
    public async Task Editar_preco_do_servico_nao_altera_agendamento_ja_marcado_nem_o_financeiro()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegundaFeira();

        var agendamentoId = await AgendarAsync(cliente, cenario, AsDataHora(segunda, new TimeOnly(10, 0)));
        (await cliente.PostAsJsonAsync("/painel/pagamentos", new RegistrarPagamento(agendamentoId, 50m, "Pix")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var servico = (await cliente.GetFromJsonAsync<List<ServicoResumo>>("/painel/servicos"))!.Single(s => s.Id == cenario.ServicoId);
        (await cliente.PutAsJsonAsync($"/painel/servicos/{servico.Id}", new AtualizarServico(
            servico.CategoriaId, "Serviço Renomeado", 80m, 45, false))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // O agendamento já marcado continua com nome, preço e duração do momento.
        var agenda = await cliente.GetFromJsonAsync<List<AgendamentoResumo>>(
            $"/painel/agenda?profissionalId={cenario.ProfissionalId}&data={segunda:yyyy-MM-dd}");
        var marcado = agenda!.Single(a => a.Id == agendamentoId);
        marcado.Total.Should().Be(50m);
        marcado.Servicos.Should().Equal("Serviço de Teste");
        (marcado.Fim - marcado.Inicio).Should().Be(TimeSpan.FromMinutes(30));

        var resumo = await cliente.GetFromJsonAsync<ResumoFinanceiro>(
            $"/painel/financeiro/resumo?inicio={segunda.AddDays(-1):yyyy-MM-dd}&fim={segunda.AddDays(1):yyyy-MM-dd}");
        resumo!.Total.Should().Be(50m);
        resumo.PorServico.Should().ContainSingle(s => s.ServicoId == servico.Id && s.Total == 50m && s.NomeServico == "Serviço de Teste");

        // Agendamento novo já usa o preço e a duração novos.
        var novoId = await AgendarAsync(cliente, cenario, AsDataHora(segunda, new TimeOnly(14, 0)));
        var agendaDepois = await cliente.GetFromJsonAsync<List<AgendamentoResumo>>(
            $"/painel/agenda?profissionalId={cenario.ProfissionalId}&data={segunda:yyyy-MM-dd}");
        var novo = agendaDepois!.Single(a => a.Id == novoId);
        novo.Total.Should().Be(80m);
        (novo.Fim - novo.Inicio).Should().Be(TimeSpan.FromMinutes(45));

        (await NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters().AnyAsync(l => l.EntidadeId == servico.Id && l.Acao == "Editar")))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Registros_sem_uso_sao_apagados_de_fato()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var profissionalId = await (await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Sem Uso")))
            .Content.ReadFromJsonAsync<Guid>();
        var usuarioId = await CriarUsuarioAsync(admin, $"sem-uso-{Guid.NewGuid():N}@teste.com", Perfil.Recepcionista);
        var categoriaId = await (await admin.PostAsJsonAsync("/painel/categorias", new { nome = "Categoria" })).Content.ReadFromJsonAsync<Guid>();
        var servicoId = await (await admin.PostAsJsonAsync("/painel/servicos", new CriarServico(categoriaId, "Serviço Sem Uso", 10m, 15)))
            .Content.ReadFromJsonAsync<Guid>();

        foreach (var rota in new[] { $"profissionais/{profissionalId}", $"usuarios/{usuarioId}", $"servicos/{servicoId}" })
        {
            var resposta = await admin.DeleteAsync($"/painel/{rota}");
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);
            (await resposta.Content.ReadFromJsonAsync<RespostaExclusao>())!.MantidoNoHistorico.Should().BeFalse();
        }

        (await NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().AnyAsync(p => p.Id == profissionalId))).Should().BeFalse();
        (await NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Id == usuarioId))).Should().BeFalse();
        (await NoBancoAsync(db => db.Servicos.IgnoreQueryFilters().AnyAsync(s => s.Id == servicoId))).Should().BeFalse();
    }

    [Fact]
    public async Task Profissional_e_servico_com_historico_sao_excluidos_logicamente_e_o_historico_mantem_o_nome()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegundaFeira();

        var agendamentoId = await AgendarAsync(admin, cenario, AsDataHora(segunda, new TimeOnly(10, 0)));
        await admin.PostAsJsonAsync("/painel/pagamentos", new RegistrarPagamento(agendamentoId, 50m, "Pix"));
        (await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/concluir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        foreach (var rota in new[] { $"profissionais/{cenario.ProfissionalId}", $"servicos/{cenario.ServicoId}" })
        {
            var previa = await admin.GetFromJsonAsync<PreviaExclusao>($"/painel/{rota}/exclusao");
            previa!.TemHistorico.Should().BeTrue();
            previa.Bloqueio.Should().BeNull();

            var resposta = await admin.DeleteAsync($"/painel/{rota}");
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);
            (await resposta.Content.ReadFromJsonAsync<RespostaExclusao>())!.MantidoNoHistorico.Should().BeTrue();
        }

        // Some das listas, das ações e da página pública...
        (await admin.GetFromJsonAsync<List<ProfissionalResumo>>("/painel/profissionais")).Should().NotContain(p => p.Id == cenario.ProfissionalId);
        (await admin.GetFromJsonAsync<List<ServicoResumo>>("/painel/servicos")).Should().NotContain(s => s.Id == cenario.ServicoId);
        (await admin.GetAsync($"/painel/profissionais/{cenario.ProfissionalId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsync($"/painel/profissionais/{cenario.ProfissionalId}/ativar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsync($"/painel/servicos/{cenario.ServicoId}/ativar", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // ...nem por ID guardado dá para agendar de novo...
        (await admin.PostAsJsonAsync("/painel/agendamentos", new CriarAgendamento(
            cenario.ProfissionalId, cenario.ClienteId, [cenario.ServicoId], AsDataHora(segunda, new TimeOnly(15, 0)))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // ...mas o histórico e o financeiro continuam mostrando quem atendeu e o que foi feito.
        var resumo = await admin.GetFromJsonAsync<ResumoFinanceiro>(
            $"/painel/financeiro/resumo?inicio={segunda.AddDays(-1):yyyy-MM-dd}&fim={segunda.AddDays(1):yyyy-MM-dd}");
        resumo!.Total.Should().Be(50m);
        resumo.PorProfissional.Should().ContainSingle(p => p.NomeProfissional == "Profissional de Teste" && p.Total == 50m);
        resumo.PorServico.Should().ContainSingle(s => s.NomeServico == "Serviço de Teste");

        (await NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().AnyAsync(p => p.Id == cenario.ProfissionalId && p.Excluido && !p.Ativo))).Should().BeTrue();
        (await NoBancoAsync(db => db.Servicos.IgnoreQueryFilters().AnyAsync(s => s.Id == cenario.ServicoId && s.Excluido && !s.Ativo))).Should().BeTrue();
    }

    [Fact]
    public async Task Dados_pessoais_de_profissional_excluido_com_historico_sao_apagados()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        (await admin.PutAsJsonAsync($"/painel/profissionais/{cenario.ProfissionalId}", new AtualizarProfissional(
            "Profissional de Teste", "+5571988887777", "pessoa@teste.com", "Barbeiro", "111.444.777-35",
            new Endereco("Rio Vermelho", "Salvador", "Rua A", "10", "41940-000"), "https://exemplo.dev/foto.jpg")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var agendamentoId = await AgendarAsync(admin, cenario, AsDataHora(ProximaSegundaFeira(), new TimeOnly(10, 0)));
        await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/cancelar", null);

        (await admin.DeleteAsync($"/painel/profissionais/{cenario.ProfissionalId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var profissional = await NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == cenario.ProfissionalId));
        profissional.Nome.Should().Be("Profissional de Teste");
        profissional.Telefone.Should().BeNull();
        profissional.Email.Should().BeNull();
        profissional.Cpf.Should().BeNull();
        profissional.FotoUrl.Should().BeNull();
        profissional.Endereco.Should().Be(Endereco.Vazio);
    }

    [Fact]
    public async Task Profissional_com_agendamentos_futuros_so_sai_depois_de_transferir_ou_cancelar_cada_um()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var segunda = ProximaSegundaFeira();

        // Cliente com e-mail, para conferir o aviso de cancelamento.
        await NoBancoAsync(async db =>
        {
            var clienteDb = await db.Clientes.IgnoreQueryFilters().FirstAsync(c => c.Id == cenario.ClienteId);
            clienteDb.PreencherEmailSeVazio("cliente@teste.com");
            return await db.SaveChangesAsync();
        });

        // Outro profissional que executa o mesmo serviço, com expediente de segunda.
        var outroId = await NoBancoAsync(async db =>
        {
            var outro = Profissional.Criar(negocioId, "Outra Profissional");
            db.Profissionais.Add(outro);
            db.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocioId, outro.Id, DiaSemana.Segunda, new TimeOnly(9, 0), new TimeOnly(18, 0)));
            db.ProfissionalServicos.Add(ProfissionalServico.Criar(negocioId, outro.Id, cenario.ServicoId));
            await db.SaveChangesAsync();
            return outro.Id;
        });

        var primeiro = await AgendarAsync(admin, cenario, AsDataHora(segunda, new TimeOnly(10, 0)));
        var segundo = await AgendarAsync(admin, cenario, AsDataHora(segunda, new TimeOnly(11, 0)));

        var bloqueada = await admin.DeleteAsync($"/painel/profissionais/{cenario.ProfissionalId}");
        bloqueada.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await bloqueada.Content.ReadAsStringAsync()).Should().Contain("operacao_bloqueada").And.Contain("2 agendamentos futuros");

        var previa = await admin.GetFromJsonAsync<PreviaExclusao>($"/painel/profissionais/{cenario.ProfissionalId}/exclusao");
        previa!.AgendamentosFuturos.Should().Be(2);
        previa.Futuros.Should().OnlyContain(f => f.ProfissionaisPossiveis.Any(p => p.Id == outroId));

        (await admin.PostAsJsonAsync($"/painel/profissionais/{cenario.ProfissionalId}/agendamentos-futuros/{primeiro}/transferir",
            new TransferirAgendamentoRequisicao(outroId))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/painel/profissionais/{cenario.ProfissionalId}/agendamentos-futuros/{segundo}/cancelar", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await NoBancoAsync(db => db.Agendamentos.IgnoreQueryFilters().AnyAsync(a => a.Id == primeiro && a.ProfissionalId == outroId)))
            .Should().BeTrue();
        _fabrica.Services.GetRequiredService<EspiaEmail>().Enviados
            .Should().Contain(e => e.Destinatario == "cliente@teste.com" && e.Assunto.StartsWith("Agendamento cancelado"));

        (await admin.DeleteAsync($"/painel/profissionais/{cenario.ProfissionalId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ninguem_exclui_o_proprio_usuario_nem_o_ultimo_administrador()
    {
        var (admin, _, adminId, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var proprio = await admin.DeleteAsync($"/painel/usuarios/{adminId}");
        proprio.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await proprio.Content.ReadAsStringAsync()).Should().Contain("próprio usuário");

        // Recepcionista com poder de gerenciar/excluir usuários tenta tirar o único Administrador.
        var emailRecepcao = $"recepcao-{Guid.NewGuid():N}@teste.com";
        var recepcaoId = await CriarUsuarioAsync(admin, emailRecepcao, Perfil.Recepcionista);
        foreach (var permissao in new[] { Permissao.GerenciarUsuarios, Permissao.ExcluirCadastros })
            await admin.PostAsync($"/painel/usuarios/{recepcaoId}/permissoes/{permissao}", null);
        var recepcao = await LogarAsync(emailRecepcao, "SenhaForte!123");

        var ultimo = await recepcao.DeleteAsync($"/painel/usuarios/{adminId}");
        ultimo.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ultimo.Content.ReadAsStringAsync()).Should().Contain("último Administrador");

        (await recepcao.DeleteAsync($"/painel/usuarios/{adminId}/permissoes/{Permissao.GerenciarUsuarios}"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await recepcao.PostAsync($"/painel/usuarios/{adminId}/desativar", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Id == adminId && u.Ativo && !u.Excluido)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Usuario_excluido_perde_o_acesso_na_hora_tem_os_dados_apagados_e_o_email_volta_a_ficar_livre()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);

        var email = $"colega-{Guid.NewGuid():N}@teste.com";
        var colegaId = await CriarUsuarioAsync(admin, email, Perfil.Administrador);
        var colega = await LogarAsync(email, "SenhaForte!123");

        // O colega faz uma edição: vira histórico (autor no log de auditoria) — exclusão lógica.
        var servico = (await colega.GetFromJsonAsync<List<ServicoResumo>>("/painel/servicos"))!.Single(s => s.Id == cenario.ServicoId);
        (await colega.PutAsJsonAsync($"/painel/servicos/{servico.Id}", new AtualizarServico(servico.CategoriaId, "Corte", 55m, 30, false)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"/painel/usuarios/{colegaId}", new AtualizarUsuario(
            "Colega", "+5571999990000", "111.444.777-35", Endereco: new Endereco("Barra", "Salvador", null, null, null),
            FotoUrl: "https://exemplo.dev/f.jpg"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resposta = await admin.DeleteAsync($"/painel/usuarios/{colegaId}");
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<RespostaExclusao>())!.MantidoNoHistorico.Should().BeTrue();

        // Mesmo token de antes: não vale mais, sem esperar os 15 minutos.
        (await colega.GetAsync("/painel/servicos")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await NoBancoAsync(db => db.TokensAtualizacao.IgnoreQueryFilters().AnyAsync(t => t.UsuarioId == colegaId && t.RevogadoEm == null)))
            .Should().BeFalse();

        var excluido = await NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == colegaId));
        excluido.Excluido.Should().BeTrue();
        excluido.Nome.Should().Be("Colega");
        excluido.Email.Should().BeEmpty();
        excluido.Telefone.Should().BeNull();
        excluido.Cpf.Should().BeNull();
        excluido.FotoUrl.Should().BeNull();
        excluido.Endereco.Should().Be(Endereco.Vazio);

        (await admin.GetFromJsonAsync<List<UsuarioResumo>>("/painel/usuarios", OpcoesJsonTeste.Opcoes)).Should().NotContain(u => u.Id == colegaId);

        // O e-mail pode ser usado de novo num cadastro futuro.
        (await admin.PostAsJsonAsync("/painel/usuarios", new CriarUsuario("Nova Pessoa", email, "SenhaForte!123", Perfil.Recepcionista)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Profissional_excluido_nao_conta_no_limite_do_plano()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        await NoBancoAsync(async db =>
        {
            var comeco = await db.Planos.FirstAsync(p => p.Nome == "Começo");
            db.Assinaturas.Add(ServicoAssinatura.IniciarTeste(negocioId, comeco, Periodicidade.Mensal, "teste", DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync();
        });

        // Um com histórico (exclusão lógica) + dois sem: o plano Começo (até 3) fica cheio.
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var agendamentoId = await AgendarAsync(admin, cenario, AsDataHora(ProximaSegundaFeira(), new TimeOnly(10, 0)));
        await admin.PostAsync($"/painel/agendamentos/{agendamentoId}/cancelar", null);
        for (var i = 0; i < 2; i++)
            (await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional($"P{i}"))).StatusCode.Should().Be(HttpStatusCode.Created);

        (await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Excedente"))).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await admin.DeleteAsync($"/painel/profissionais/{cenario.ProfissionalId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Novo"))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Sem_as_permissoes_de_editar_e_excluir_cadastros_a_api_responde_403()
    {
        // Pode ver e gerenciar os três cadastros, mas não recebeu editar nem excluir (seção 7).
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(
            Perfil.Recepcionista, Permissao.GerenciarUsuarios, Permissao.GerenciarProfissionais, Permissao.GerenciarServicos);
        var cenario = await _fabrica.CriarCenarioPadraoAsync(negocioId);
        var categoriaId = await NoBancoAsync(db => db.Servicos.IgnoreQueryFilters().Where(s => s.Id == cenario.ServicoId).Select(s => s.CategoriaId).FirstAsync());
        var outroUsuario = await CriarUsuarioAsync(cliente, $"outro-{Guid.NewGuid():N}@teste.com", Perfil.Profissional);

        (await cliente.PutAsJsonAsync($"/painel/servicos/{cenario.ServicoId}", new AtualizarServico(categoriaId, "X", 1m, 10, false)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cliente.PutAsJsonAsync($"/painel/profissionais/{cenario.ProfissionalId}", new AtualizarProfissional("X", null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cliente.PutAsJsonAsync($"/painel/usuarios/{outroUsuario}", new AtualizarUsuario("X", null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (var rota in new[] { $"servicos/{cenario.ServicoId}", $"profissionais/{cenario.ProfissionalId}", $"usuarios/{outroUsuario}" })
        {
            (await cliente.DeleteAsync($"/painel/{rota}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await cliente.GetAsync($"/painel/{rota}/exclusao")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }
}
