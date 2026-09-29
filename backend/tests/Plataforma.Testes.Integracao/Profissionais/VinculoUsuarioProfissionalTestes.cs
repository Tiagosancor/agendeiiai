using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Profissionais;

/// <summary>Vínculo entre usuário (quem faz login) e profissional (quem aparece na agenda) — seção 7, item 11 da seção 14.</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class VinculoUsuarioProfissionalTestes : IAsyncLifetime
{
    private const string Senha = "SenhaDoProfissional1";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public VinculoUsuarioProfissionalTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Criar_profissional_com_acesso_gera_usuario_e_profissional_vinculados_numa_so_transacao()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var email = Email();

        var criado = await admin.PostAsJsonAsync("/painel/profissionais",
            new CriarProfissional("Joana", "(71) 98888-1111", Acesso: new AcessoProfissional(email, Senha, EnviarConvite: false)));
        criado.StatusCode.Should().Be(HttpStatusCode.Created);
        var profissionalId = await criado.Content.ReadFromJsonAsync<Guid>();

        var usuario = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Email == email));
        usuario.Perfil.Should().Be(Perfil.Profissional);
        usuario.ProfissionalId.Should().Be(profissionalId);
        usuario.Nome.Should().Be("Joana");

        var detalhe = await admin.GetFromJsonAsync<ProfissionalDetalhe>($"/painel/profissionais/{profissionalId}");
        detalhe!.Acesso.Should().BeEquivalentTo(new AcessoVinculado(usuario.Id, email, true));
        (await LogarAsync(email, Senha)).Should().Be(HttpStatusCode.OK);

        // E-mail já usado: nada é gravado — nem o profissional, nem o usuário.
        var repetido = await admin.PostAsJsonAsync("/painel/profissionais",
            new CriarProfissional("Outra Joana", Acesso: new AcessoProfissional(email, Senha, EnviarConvite: false)));
        repetido.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _fabrica.NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().AnyAsync(p => p.Nome == "Outra Joana"))).Should().BeFalse();

        var senhaCurta = await admin.PostAsJsonAsync("/painel/profissionais",
            new CriarProfissional("Sem Senha", Acesso: new AcessoProfissional(Email(), "123", EnviarConvite: false)));
        senhaCurta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _fabrica.NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().AnyAsync(p => p.Nome == "Sem Senha"))).Should().BeFalse();
    }

    [Fact]
    public async Task Usuario_criado_com_perfil_Profissional_e_vinculado_ao_salvar_o_profissional_e_nao_vincula_a_dois()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var email = Email();

        var usuarioId = await (await admin.PostAsJsonAsync("/painel/usuarios", new CriarUsuario("Carlos", email, Senha, Perfil.Profissional)))
            .Content.ReadFromJsonAsync<Guid>();

        var criado = await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Carlos", Email: email, UsuarioId: usuarioId));
        criado.StatusCode.Should().Be(HttpStatusCode.Created);
        var profissionalId = await criado.Content.ReadFromJsonAsync<Guid>();
        (await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Id == usuarioId)))
            .ProfissionalId.Should().Be(profissionalId);

        // O mesmo usuário num segundo profissional, e um segundo acesso para o mesmo profissional: recusados.
        (await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Carlos de novo", UsuarioId: usuarioId)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync($"/painel/profissionais/{profissionalId}/acesso", new AcessoProfissional(Email(), Senha, false)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _fabrica.NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().CountAsync(p => p.Nome.StartsWith("Carlos")))).Should().Be(1);

        // O banco também segura: um segundo usuário apontando para o mesmo profissional viola o índice único.
        var violou = await _fabrica.NoBancoAsync(async db =>
        {
            var outro = await db.Usuarios.IgnoreQueryFilters().FirstAsync(u => u.Id != usuarioId && u.ProfissionalId == null);
            outro.VincularProfissional(profissionalId);
            try
            {
                await db.SaveChangesAsync();
                return false;
            }
            catch (DbUpdateException)
            {
                return true;
            }
        });
        violou.Should().BeTrue();
    }

    [Fact]
    public async Task Dar_acesso_depois_com_convite_por_email_e_sem_permissao_de_usuarios_recebe_403()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var profissionalId = await (await admin.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Rita")))
            .Content.ReadFromJsonAsync<Guid>();
        (await admin.GetFromJsonAsync<ProfissionalDetalhe>($"/painel/profissionais/{profissionalId}"))!.Acesso.Should().BeNull();

        // Quem gerencia profissionais mas não usuários não cria login.
        using var gerente = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista,
            permissoesExtras: [Permissao.GerenciarProfissionais]);
        (await gerente.PostAsJsonAsync($"/painel/profissionais/{profissionalId}/acesso", new AcessoProfissional(Email(), Senha, false)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await gerente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Outro", Acesso: new AcessoProfissional(Email(), Senha, false))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await gerente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional("Sem acesso"))).StatusCode.Should().Be(HttpStatusCode.Created);

        var email = Email();
        var espia = _fabrica.Services.GetRequiredService<EspiaEmail>();
        var acesso = await admin.PostAsJsonAsync($"/painel/profissionais/{profissionalId}/acesso", new AcessoProfissional(email, null, EnviarConvite: true));
        acesso.StatusCode.Should().Be(HttpStatusCode.Created);

        var convite = espia.Enviados.Should().ContainSingle(e => e.Destinatario == email).Subject;
        convite.Assunto.Should().StartWith("Seu acesso");
        convite.CorpoHtml.Should().Contain("/painel/redefinir-senha?token=");
        (await admin.GetFromJsonAsync<ProfissionalDetalhe>($"/painel/profissionais/{profissionalId}"))!.Acesso!.Email.Should().Be(email);
    }

    [Fact]
    public async Task Excluir_o_usuario_nao_apaga_o_profissional_e_excluir_o_profissional_tira_o_acesso_do_usuario()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var emailUm = Email();
        var profissionalUm = await (await admin.PostAsJsonAsync("/painel/profissionais",
            new CriarProfissional("Profissional Um", Acesso: new AcessoProfissional(emailUm, Senha, false)))).Content.ReadFromJsonAsync<Guid>();
        var usuarioUm = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Email == emailUm));

        (await admin.DeleteAsync($"/painel/usuarios/{usuarioUm.Id}")).IsSuccessStatusCode.Should().BeTrue();
        var profissional = await _fabrica.NoBancoAsync(db => db.Profissionais.IgnoreQueryFilters().SingleAsync(p => p.Id == profissionalUm));
        profissional.Excluido.Should().BeFalse();
        profissional.Ativo.Should().BeTrue();
        (await admin.GetFromJsonAsync<List<ProfissionalResumo>>("/painel/profissionais"))!.Should().Contain(p => p.Id == profissionalUm);

        var emailDois = Email();
        var profissionalDois = await (await admin.PostAsJsonAsync("/painel/profissionais",
            new CriarProfissional("Profissional Dois", Acesso: new AcessoProfissional(emailDois, Senha, false)))).Content.ReadFromJsonAsync<Guid>();
        (await LogarAsync(emailDois, Senha)).Should().Be(HttpStatusCode.OK);

        (await admin.DeleteAsync($"/painel/profissionais/{profissionalDois}")).IsSuccessStatusCode.Should().BeTrue();

        var usuarioDois = await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().SingleAsync(u => u.Email == emailDois));
        usuarioDois.Ativo.Should().BeFalse();
        usuarioDois.Excluido.Should().BeFalse(); // excluir o usuário junto só por escolha explícita, em "Usuários"
        usuarioDois.ProfissionalId.Should().BeNull();
        (await LogarAsync(emailDois, Senha)).Should().NotBe(HttpStatusCode.OK);
    }

    private static string Email() => $"prof-{Guid.NewGuid():N}@teste.com";

    private async Task<HttpStatusCode> LogarAsync(string email, string senha)
    {
        using var cliente = _fabrica.CreateClient();
        return (await cliente.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, senha))).StatusCode;
    }
}
