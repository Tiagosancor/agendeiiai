using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <summary>
    /// Busca de clientes por nome sem acento e por parte do telefone (agenda do painel). Só SQL, sem mudança de modelo:
    /// extensões <c>unaccent</c> e <c>pg_trgm</c>, a função imutável <c>sem_acento</c> (o <c>unaccent</c> puro é STABLE e não
    /// serve em índice) e dois índices trigram. Aditiva: nenhuma tabela ou coluna existente muda.
    /// </summary>
    public partial class BuscaClientesAgenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION sem_acento(texto text) RETURNS text
                AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, texto) $$
                LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_clientes_nome_busca ON clientes USING gin (sem_acento(lower(nome)) gin_trgm_ops);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_clientes_telefone_busca ON clientes USING gin (telefone gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // As extensões ficam: outras estruturas podem passar a depender delas e removê-las exige privilégio extra.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_clientes_telefone_busca;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_clientes_nome_busca;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sem_acento(text);");
        }
    }
}
