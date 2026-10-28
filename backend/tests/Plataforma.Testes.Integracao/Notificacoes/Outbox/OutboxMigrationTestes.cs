using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Notificacoes.Outbox;

/// <summary>
/// B7.4-B: a migration da outbox é aditiva. Sobe (tabela, índices, constraints, FK), desce sem tocar nas outras tabelas, sobe de novo.
/// A versão antiga da API, que não conhece a tabela, continua funcionando com ela presente (nenhuma coluna ou tabela existente muda).
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class OutboxMigrationTestes(PostgresContainerFixture postgres)
{
    private const string MigrationAnterior = "20261013180652_BuscaClientesAgenda";

    [Fact]
    public async Task Migration_sobe_cria_a_tabela_com_indices_e_constraints_e_desce_sem_tocar_nas_outras()
    {
        var banco = $"outbox_migracao_{Guid.NewGuid():N}";
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
            using var aquecimento = fabrica.CreateClient(); // Development aplica as migrations pendentes ao subir (Up)

            (await Consultar<string>(cs, "SELECT migration_id FROM \"__EFMigrationsHistory\" WHERE migration_id LIKE '%OutboxNotificacoes'")).Should().ContainSingle();

            (await Consultar<string>(cs, "SELECT column_name FROM information_schema.columns WHERE table_name = 'notificacoes_pendentes' ORDER BY column_name"))
                .Should().BeEquivalentTo([
                    "agendamento_id", "atualizado_em", "canal", "cancelada_em", "chave_deduplicacao", "cliente_id", "criado_em", "enviada_em",
                    "envio_iniciado_em", "expira_em", "id", "id_mensagem_provedor", "lease_ate", "negocio_id", "profissional_id",
                    "proxima_tentativa_em", "reivindicacao_id", "status", "tentativas", "tipo", "ultimo_erro_codigo", "versao_horario"]);

            var indices = await Consultar<string>(cs, "SELECT indexdef FROM pg_indexes WHERE tablename = 'notificacoes_pendentes'");
            indices.Should().Contain(d => d.Contains("UNIQUE") && d.Contains("(negocio_id, chave_deduplicacao, canal)"), "deduplicação por tenant e canal");
            indices.Should().Contain(d => d.Contains("(proxima_tentativa_em, id)") && d.Contains("WHERE") && d.Contains("'Pendente'"), "índice parcial do despachante");
            indices.Should().Contain(d => d.Contains("(lease_ate)") && d.Contains("'Enviando'"), "índice parcial da recuperação de lease");
            indices.Should().Contain(d => d.Contains("(status, atualizado_em)"), "contrato administrativo e retenção");

            (await Consultar<string>(cs, "SELECT conname FROM pg_constraint WHERE conrelid = 'notificacoes_pendentes'::regclass ORDER BY conname"))
                .Should().Contain(["ck_notificacoes_pendentes_lease", "ck_notificacoes_pendentes_tentativas", "fk_notificacoes_pendentes_negocios_negocio_id", "pk_notificacoes_pendentes"]);
            (await Consultar<string>(cs, "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'fk_notificacoes_pendentes_negocios_negocio_id'"))
                .Should().ContainSingle().Which.Should().Be("c", "cascade ao apagar o negócio");

            // As tabelas do agente de WhatsApp vêm de uma migration POSTERIOR: descer até aqui também as remove.
            var tabelasAntes = await Consultar<string>(cs, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> 'notificacoes_pendentes' AND table_name NOT IN ('instancias_whatsapp', 'conversas_whatsapp', 'mensagens_whatsapp', 'usos_ia', 'pendencias_agendamento_whatsapp', 'rodadas_agente') ORDER BY table_name");
            var colunasAntes = await Consultar<string>(cs,
                "SELECT table_name || '.' || column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name <> 'notificacoes_pendentes' AND table_name NOT IN ('instancias_whatsapp', 'conversas_whatsapp', 'mensagens_whatsapp', 'usos_ia', 'pendencias_agendamento_whatsapp', 'rodadas_agente') ORDER BY 1");

            // Down: volta para a migration anterior — só a tabela da outbox some.
            using (var escopo = fabrica.Services.CreateScope())
            {
                var migrador = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().GetService<IMigrator>();
                await migrador.MigrateAsync(MigrationAnterior);
            }

            (await Consultar<string>(cs, "SELECT table_name FROM information_schema.tables WHERE table_name = 'notificacoes_pendentes'")).Should().BeEmpty();
            (await Consultar<string>(cs, "SELECT migration_id FROM \"__EFMigrationsHistory\" WHERE migration_id LIKE '%OutboxNotificacoes'")).Should().BeEmpty();
            (await Consultar<string>(cs, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name"))
                .Should().BeEquivalentTo(tabelasAntes, "o Down só remove a tabela desta fase");
            (await Consultar<string>(cs, "SELECT table_name || '.' || column_name FROM information_schema.columns WHERE table_schema = 'public' ORDER BY 1"))
                .Should().BeEquivalentTo(colunasAntes, "nenhuma coluna das outras tabelas muda");

            // Up de novo: idempotente em banco que já passou por ele.
            using (var escopo = fabrica.Services.CreateScope())
                await escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().Database.MigrateAsync();
            (await Consultar<string>(cs, "SELECT table_name FROM information_schema.tables WHERE table_name = 'notificacoes_pendentes'")).Should().ContainSingle();
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

    [Fact]
    public async Task Tabela_aceita_o_check_do_lease_e_recusa_estado_inconsistente()
    {
        await using var fabrica = new PlataformaWebApplicationFactory(postgres.ConnectionString);
        using var aquecimento = fabrica.CreateClient();
        await fabrica.ResetarBancoAsync();
        var negocioId = await fabrica.NoBancoAsync(async db =>
        {
            var negocio = Plataforma.Dominio.Negocios.Negocio.Criar(
                Plataforma.Dominio.Negocios.Slug.Criar("chk-" + Guid.NewGuid().ToString("N")[..12]), "Negócio", Plataforma.Dominio.Negocios.TipoNegocio.Barbearia);
            db.Negocios.Add(negocio);
            await db.SaveChangesAsync();
            return negocio.Id;
        });

        async Task<int> InserirAsync(string status, Guid? token, DateTimeOffset? lease) => await fabrica.NoBancoAsync(db => db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO notificacoes_pendentes (id, negocio_id, tipo, canal, chave_deduplicacao, status, tentativas, proxima_tentativa_em, expira_em, lease_ate, reivindicacao_id, criado_em)
            VALUES ({Guid.NewGuid()}, {negocioId}, 'Confirmacao', 'Email', {"k-" + Guid.NewGuid()}, {status}, 0, now(), now(), {lease}::timestamptz, {token}::uuid, now())"));

        (await InserirAsync("Pendente", null, null)).Should().Be(1);
        (await InserirAsync("Enviando", Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2))).Should().Be(1);

        // "Enviando" sem token, ou "Pendente" com token: o banco recusa (a reivindicação e o status andam juntos).
        var semToken = () => InserirAsync("Enviando", null, DateTimeOffset.UtcNow);
        var tokenSemEnviando = () => InserirAsync("Pendente", Guid.NewGuid(), null);
        (await semToken.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("ck_notificacoes_pendentes_lease");
        (await tokenSemEnviando.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("ck_notificacoes_pendentes_lease");
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
