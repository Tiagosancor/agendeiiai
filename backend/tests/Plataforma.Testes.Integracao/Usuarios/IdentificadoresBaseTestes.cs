using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Usuarios;

/// <summary>
/// Fatia A do login por e-mail, telefone ou nome de usuário: a base (colunas, índices, checks, claims, rótulo da auditoria,
/// "contratar" sem e-mail verificado e a migration que antecede o deploy).
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class IdentificadoresBaseTestes : IAsyncLifetime
{
    private const string MigrationAnterior = "20261029141227_AgenteWhatsAppRemarcar";
    private const string MigrationDosIdentificadores = "20261116130214_IdentificadoresLoginUsuario";
    private const string MigrationLimpeza = "20261116132558_IdentificadoresLoginLimpeza";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public IdentificadoresBaseTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private async Task<(HttpClient Cliente, Guid NegocioId, Guid UsuarioId, string Token)> AdministradorAsync(bool emailVerificado)
    {
        var email = $"adm-{Guid.NewGuid():N}@teste.com";
        var (negocioId, usuarioId) = await _fabrica.NoBancoAsync(async db =>
        {
            var negocio = Negocio.Criar(Slug.Criar("ib-" + Guid.NewGuid().ToString("N")[..12]), "Negócio", TipoNegocio.Barbearia);
            db.Negocios.Add(negocio);
            var hasher = _fabrica.Services.GetRequiredService<ISenhaHasher>();
            var usuario = Usuario.Criar(negocio.Id, "Dona do Teste", email, Perfil.Administrador, hasher.Hash(SemeadorDeUsuarios.SenhaPadrao));
            if (emailVerificado)
                usuario.ConfirmarEmail(email, DateTimeOffset.UtcNow);
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            return (negocio.Id, usuario.Id);
        });

        using var anonimo = _fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SemeadorDeUsuarios.SenhaPadrao));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<RespostaLogin>())!.AccessToken;

        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (cliente, negocioId, usuarioId, token);
    }

    [Fact]
    public async Task Claim_email_so_existe_com_email_verificado_e_o_token_traz_o_nome()
    {
        var (_, _, _, tokenVerificado) = await AdministradorAsync(emailVerificado: true);
        var (_, _, _, tokenNaoVerificado) = await AdministradorAsync(emailVerificado: false);

        var verificado = new JwtSecurityTokenHandler().ReadJwtToken(tokenVerificado);
        verificado.Claims.Should().Contain(c => c.Type == "email");
        verificado.Claims.Should().Contain(c => c.Type == "name" && c.Value == "Dona do Teste");

        var naoVerificado = new JwtSecurityTokenHandler().ReadJwtToken(tokenNaoVerificado);
        naoVerificado.Claims.Should().NotContain(c => c.Type == "email");
        naoVerificado.Claims.Should().Contain(c => c.Type == "name" && c.Value == "Dona do Teste");
    }

    [Fact]
    public async Task Auditoria_de_quem_nao_tem_email_verificado_nao_da_500_e_o_autor_vira_o_nome()
    {
        var (cliente, negocioId, usuarioId, _) = await AdministradorAsync(emailVerificado: false);

        var resposta = await cliente.PutAsJsonAsync($"/painel/usuarios/{usuarioId}", new AtualizarUsuario("Dona Renomeada", null, null));

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var log = await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .FirstAsync(l => l.NegocioId == negocioId && l.EntidadeId == usuarioId));
        log.Autor.Should().Be("Dona do Teste"); // o nome do token (antes da edição), não o e-mail
    }

    [Fact]
    public async Task Auditoria_de_quem_tem_email_verificado_continua_com_o_email_como_autor()
    {
        var (cliente, negocioId, usuarioId, _) = await AdministradorAsync(emailVerificado: true);

        (await cliente.PutAsJsonAsync($"/painel/usuarios/{usuarioId}", new AtualizarUsuario("Dona Renomeada", null, null)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var log = await _fabrica.NoBancoAsync(db => db.LogsAuditoriaNegocio.IgnoreQueryFilters()
            .FirstAsync(l => l.NegocioId == negocioId && l.EntidadeId == usuarioId));
        log.Autor.Should().Contain("@teste.com");
    }

    [Fact]
    public async Task Contratar_sem_email_verificado_e_400_com_codigo_email_necessario()
    {
        var (cliente, _, _, _) = await AdministradorAsync(emailVerificado: false);

        var resposta = await cliente.PostAsJsonAsync("/painel/assinatura/contratar",
            new AssinaturaController.ContratarRequisicao(Guid.NewGuid(), Plataforma.Dominio.Assinaturas.Periodicidade.Mensal, "529.982.247-25"));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("email_necessario");
    }

    [Fact]
    public async Task Banco_exige_algum_identificador_e_garante_unicidade_do_nome_de_usuario_e_do_telefone_de_login()
    {
        var (_, _, usuarioId, _) = await AdministradorAsync(emailVerificado: true);

        async Task<PostgresException> ExecutarAsync(string sql)
        {
            await using var conexao = new NpgsqlConnection(_postgres.ConnectionString);
            await conexao.OpenAsync();
            await using var comando = new NpgsqlCommand(sql, conexao);
            return await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
        }

        // Sem nenhum identificador (não excluído): recusado pelo check.
        (await ExecutarAsync($"UPDATE usuarios SET email = NULL WHERE id = '{usuarioId}'")).ConstraintName
            .Should().Be("ck_usuarios_algum_identificador");

        // Formato do nome de usuário (defesa em profundidade): só dígitos, sinais seguidos, maiúscula, curto.
        foreach (var invalido in new[] { "12345", "ab..cd", "Abcd", "ab" })
            (await ExecutarAsync($"UPDATE usuarios SET nome_usuario = '{invalido}' WHERE id = '{usuarioId}'")).ConstraintName
                .Should().Be("ck_usuarios_nome_usuario_formato");

        // Único no sistema inteiro: outro negócio não pode repetir.
        await _fabrica.NoBancoAsync(async db =>
        {
            var u = await db.Usuarios.IgnoreQueryFilters().FirstAsync(x => x.Id == usuarioId);
            u.DefinirNomeUsuario(NomeUsuario.Criar("ana.silva"));
            u.DefinirTelefoneLogin(TelefoneE164.Criar("(71) 8888-7777"));
            await db.SaveChangesAsync();
            return 0;
        });

        var (_, _, outroId, _) = await AdministradorAsync(emailVerificado: true);
        (await ExecutarAsync($"UPDATE usuarios SET nome_usuario = 'ana.silva' WHERE id = '{outroId}'")).ConstraintName
            .Should().Be("ix_usuarios_nome_usuario");
        (await ExecutarAsync($"UPDATE usuarios SET telefone_login = '+5571988887777' WHERE id = '{outroId}'")).ConstraintName
            .Should().Be("ix_usuarios_telefone_login");

        // O telefone de login foi guardado na forma canônica (com o 9).
        (await _fabrica.NoBancoAsync(db => db.Usuarios.IgnoreQueryFilters().Where(x => x.Id == usuarioId)
            .Select(x => x.TelefoneLogin!.Valor).FirstAsync())).Should().Be("+5571988887777");
    }

    [Fact]
    public async Task Excluir_usuario_libera_o_nome_de_usuario_para_outra_conta()
    {
        var (_, _, usuarioId, _) = await AdministradorAsync(emailVerificado: true);
        var (_, _, outroId, _) = await AdministradorAsync(emailVerificado: true);

        await _fabrica.NoBancoAsync(async db =>
        {
            var u = await db.Usuarios.IgnoreQueryFilters().FirstAsync(x => x.Id == usuarioId);
            u.DefinirNomeUsuario(NomeUsuario.Criar("liberado"));
            await db.SaveChangesAsync();
            u.Excluir(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();

            var outro = await db.Usuarios.IgnoreQueryFilters().FirstAsync(x => x.Id == outroId);
            outro.DefinirNomeUsuario(NomeUsuario.Criar("liberado"));
            await db.SaveChangesAsync();
            return 0;
        });
    }

    [Fact]
    public async Task Migration_marca_emails_existentes_como_verificados_nao_deixa_nulo_em_quem_ja_existe_e_e_reversivel()
    {
        var banco = $"identificadores_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var criar = new NpgsqlCommand($"CREATE DATABASE {banco}", admin);
            await criar.ExecuteNonQueryAsync();
        }

        var cs = new NpgsqlConnectionStringBuilder(_postgres.ConnectionString) { Database = banco, Pooling = false }.ConnectionString;
        try
        {
            await using var fabrica = new PlataformaWebApplicationFactory(cs);
            using var aquecimento = fabrica.CreateClient(); // Development aplica as migrations pendentes ao subir

            var (ativoId, excluidoId) = await fabrica.NoBancoAsync(async db =>
            {
                var negocio = Negocio.Criar(Slug.Criar("mig-" + Guid.NewGuid().ToString("N")[..12]), "Negócio", TipoNegocio.Barbearia);
                db.Negocios.Add(negocio);
                var ativo = Usuario.Criar(negocio.Id, "Ativo", $"a-{Guid.NewGuid():N}@teste.com", Perfil.Administrador, "hash");
                var excluido = Usuario.Criar(negocio.Id, "Excluído", $"e-{Guid.NewGuid():N}@teste.com", Perfil.Recepcionista, "hash");
                db.Usuarios.AddRange(ativo, excluido);
                await db.SaveChangesAsync();
                return (ativo.Id, excluido.Id);
            });

            // Volta ao schema antigo e deixa os dados como a versão anterior os grava ("" nos excluídos).
            await MigrarAsync(fabrica, MigrationAnterior);
            await ExecutarAsync(cs, $"UPDATE usuarios SET excluido = true, email = '' WHERE id = '{excluidoId}'");

            // A migration nova não pode deixar nenhum e-mail nulo (a versão no ar o lê como não anulável).
            await MigrarAsync(fabrica, MigrationDosIdentificadores);
            (await Consultar<bool>(cs, $"SELECT email_verificado_em IS NOT NULL FROM usuarios WHERE id = '{ativoId}'")).Single().Should().BeTrue();
            (await Consultar<bool>(cs, $"SELECT email_verificado_em IS NULL FROM usuarios WHERE id = '{excluidoId}'")).Single().Should().BeTrue();
            (await Consultar<long>(cs, "SELECT count(*) FROM usuarios WHERE email IS NULL")).Single().Should().Be(0);
            (await Consultar<string>(cs, $"SELECT email FROM usuarios WHERE id = '{excluidoId}'")).Single().Should().BeEmpty();

            // Tabelas novas e colunas de redefinição existem; Down e Up de novo funcionam.
            (await Consultar<string>(cs, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'"))
                .Should().Contain(["tentativas_login", "verificacoes_email_usuario"]);
            (await Consultar<string>(cs, "SELECT column_name FROM information_schema.columns WHERE table_name = 'redefinicoes_senha'"))
                .Should().Contain(["tipo", "email_destino_hash"]);

            await MigrarAsync(fabrica, MigrationAnterior);
            (await Consultar<string>(cs, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'"))
                .Should().NotContain(["tentativas_login", "verificacoes_email_usuario"]);
            await MigrarAsync(fabrica, MigrationDosIdentificadores);
            (await Consultar<bool>(cs, $"SELECT email_verificado_em IS NOT NULL FROM usuarios WHERE id = '{ativoId}'")).Single().Should().BeTrue();

            // Rollback de schema com várias contas SEM e-mail (a pré-condição que antes quebrava o Down): não pode falhar.
            await MigrarAsync(fabrica, MigrationDosIdentificadores);
            await fabrica.NoBancoAsync(async db =>
            {
                var negocio = await db.Negocios.IgnoreQueryFilters().FirstAsync();
                db.Usuarios.Add(Usuario.Criar(negocio.Id, "Sem 1", null, NomeUsuario.Criar("sem.um"), null, Perfil.Recepcionista, "hash"));
                db.Usuarios.Add(Usuario.Criar(negocio.Id, "Sem 2", null, null, TelefoneE164.Criar("71988880001"), Perfil.Recepcionista, "hash"));
                await db.SaveChangesAsync();
                return 0;
            });
            await MigrarAsync(fabrica, MigrationAnterior);
            (await Consultar<long>(cs, "SELECT count(*) FROM usuarios WHERE email = ''")).Single().Should().BeGreaterThanOrEqualTo(2);
            // Subir de novo exige que ninguém esteja sem e-mail (o Down apagou as colunas dos outros identificadores): quem operar o
            // rollback de schema precisa dar e-mail ou excluir essas contas antes. Aqui, apagamos as duas.
            await ExecutarAsync(cs, "DELETE FROM usuarios WHERE email = '' AND excluido = false");
            await MigrarAsync(fabrica, MigrationDosIdentificadores);

            // Parte 2 (depois do deploy): o "" dos excluídos vira NULL, e o Down devolve o "" que a versão antiga espera.
            await MigrarAsync(fabrica, MigrationLimpeza);
            (await Consultar<bool>(cs, $"SELECT email IS NULL FROM usuarios WHERE id = '{excluidoId}'")).Single().Should().BeTrue();
            (await Consultar<bool>(cs, $"SELECT email IS NOT NULL FROM usuarios WHERE id = '{ativoId}'")).Single().Should().BeTrue();
            await MigrarAsync(fabrica, MigrationDosIdentificadores);
            (await Consultar<string>(cs, $"SELECT email FROM usuarios WHERE id = '{excluidoId}'")).Single().Should().BeEmpty();
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_postgres.ConnectionString);
            await admin.OpenAsync();
            await using var apagar = new NpgsqlCommand($"DROP DATABASE IF EXISTS {banco} WITH (FORCE)", admin);
            await apagar.ExecuteNonQueryAsync();
        }
    }

    private static async Task MigrarAsync(PlataformaWebApplicationFactory fabrica, string? alvo)
    {
        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(alvo);
    }

    private static async Task ExecutarAsync(string cs, string sql)
    {
        await using var conexao = new NpgsqlConnection(cs);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task<List<T>> Consultar<T>(string cs, string sql)
    {
        await using var conexao = new NpgsqlConnection(cs);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        var itens = new List<T>();
        await using var leitor = await comando.ExecuteReaderAsync();
        while (await leitor.ReadAsync())
            itens.Add(leitor.GetFieldValue<T>(0));
        return itens;
    }
}
