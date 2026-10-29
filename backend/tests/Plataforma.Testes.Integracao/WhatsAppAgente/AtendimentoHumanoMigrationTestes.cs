using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Fase 4, Fatia 3: as duas migrations do modo Humano. A das colunas é aditiva (roda ANTES do deploy); a da permissão concede
/// <c>AtenderConversasWhatsApp</c> só aos Administradores (roda DEPOIS do deploy) e desfaz no Down, sem mexer nas outras permissões.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class AtendimentoHumanoMigrationTestes(PostgresContainerFixture postgres)
{
    private const string Anterior = "20261028155133_AgenteWhatsAppLgpdIndice";
    private const string Colunas = "20261028191733_AgenteWhatsAppHumano";
    private const string Permissao_ = "20261028191802_AgenteWhatsAppAtenderPermissao";

    private static readonly string[] ColunasNovas =
        ["atendente_avisado_em", "devolvida_em", "humano_ate", "humano_desde", "humano_motivo", "humano_por_usuario_id"];

    [Fact]
    public async Task Colunas_sao_aditivas_e_a_permissao_vai_so_aos_Administradores_e_desfaz_sem_tocar_nas_outras()
    {
        var banco = $"humano_migracao_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var criar = new NpgsqlCommand($"CREATE DATABASE {banco}", admin);
            await criar.ExecuteNonQueryAsync();
        }

        var cs = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = banco, Pooling = false }.ConnectionString;
        try
        {
            await using var fabrica = new PlataformaWebApplicationFactory(cs);
            using var aquecimento = fabrica.CreateClient(); // Development aplica as migrations pendentes ao subir

            (await Consultar<string>(cs, "SELECT column_name FROM information_schema.columns WHERE table_name = 'conversas_whatsapp' ORDER BY column_name"))
                .Should().Contain(ColunasNovas);

            var (administradorId, recepcionistaId) = await fabrica.NoBancoAsync(async db =>
            {
                var negocio = Negocio.Criar(Slug.Criar("hm-" + Guid.NewGuid().ToString("N")[..12]), "Negócio", TipoNegocio.Barbearia);
                db.Negocios.Add(negocio);
                var administrador = Usuario.Criar(negocio.Id, "Admin", $"a-{Guid.NewGuid():N}@teste.com", Perfil.Administrador, "hash");
                var recepcionista = Usuario.Criar(negocio.Id, "Recepção", $"r-{Guid.NewGuid():N}@teste.com", Perfil.Recepcionista, "hash");
                db.Usuarios.AddRange(administrador, recepcionista);
                await db.SaveChangesAsync();
                return (administrador.Id, recepcionista.Id);
            });

            async Task<long> PermissoesAsync(Guid usuarioId, string? so = null) => (await Consultar<long>(cs,
                $"SELECT count(*) FROM usuario_permissoes WHERE usuario_id = '{usuarioId}'" + (so is null ? "" : $" AND permissao = '{so}'"))).Single();

            // Usuário criado pelo código novo já nasce com a permissão (o Administrador tem todas); a Recepcionista não.
            var totalDoAdministrador = await PermissoesAsync(administradorId);
            (await PermissoesAsync(administradorId, "AtenderConversasWhatsApp")).Should().Be(1);
            (await PermissoesAsync(recepcionistaId, "AtenderConversasWhatsApp")).Should().Be(0);

            // Down da permissão: sai só ela.
            await MigrarAsync(fabrica, Colunas);
            (await PermissoesAsync(administradorId, "AtenderConversasWhatsApp")).Should().Be(0);
            (await PermissoesAsync(administradorId)).Should().Be(totalDoAdministrador - 1, "as outras permissões ficam");

            // Up: o Administrador volta a ter, a Recepcionista continua sem.
            await MigrarAsync(fabrica, Permissao_);
            (await PermissoesAsync(administradorId, "AtenderConversasWhatsApp")).Should().Be(1);
            (await PermissoesAsync(recepcionistaId, "AtenderConversasWhatsApp")).Should().Be(0);
            (await PermissoesAsync(administradorId)).Should().Be(totalDoAdministrador);

            // Down das colunas: somem as seis; Up de novo: voltam (aditiva e reversível).
            await MigrarAsync(fabrica, Anterior);
            (await Consultar<string>(cs, "SELECT column_name FROM information_schema.columns WHERE table_name = 'conversas_whatsapp'"))
                .Should().NotContain(ColunasNovas);
            await MigrarAsync(fabrica, Permissao_);
            (await Consultar<string>(cs, "SELECT column_name FROM information_schema.columns WHERE table_name = 'conversas_whatsapp'"))
                .Should().Contain(ColunasNovas);
            (await PermissoesAsync(administradorId, "AtenderConversasWhatsApp")).Should().Be(1);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(postgres.ConnectionString);
            await admin.OpenAsync();
            await using var apagar = new NpgsqlCommand($"DROP DATABASE IF EXISTS {banco} WITH (FORCE)", admin);
            await apagar.ExecuteNonQueryAsync();
        }
    }

    private static async Task MigrarAsync(PlataformaWebApplicationFactory fabrica, string alvo)
    {
        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(alvo);
    }

    private static async Task<List<T>> Consultar<T>(string connectionString, string sql)
    {
        await using var conexao = new NpgsqlConnection(connectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        var itens = new List<T>();
        await using var leitor = await comando.ExecuteReaderAsync();
        while (await leitor.ReadAsync())
            itens.Add(leitor.GetFieldValue<T>(0));
        return itens;
    }
}
