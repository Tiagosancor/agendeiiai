using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class EditarExcluirCadastros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usuarios_email",
                table: "usuarios");

            migrationBuilder.AddColumn<bool>(
                name: "excluido",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "excluido_em",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "excluido",
                table: "servicos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "excluido_em",
                table: "servicos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "excluido",
                table: "profissionais",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "excluido_em",
                table: "profissionais",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "logs_auditoria_negocio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    autor = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    acao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entidade = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entidade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    detalhes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logs_auditoria_negocio", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true,
                filter: "excluido = false");

            // Seção 7: "editar/excluir cadastros" são do Administrador por padrão. Quem já é
            // Administrador ganha as duas agora (as contas novas ganham pelo PermissoesPadrao).
            migrationBuilder.Sql("""
                INSERT INTO usuario_permissoes (id, negocio_id, usuario_id, permissao, criado_em)
                SELECT gen_random_uuid(), u.negocio_id, u.id, p.permissao, now()
                FROM usuarios u
                CROSS JOIN (VALUES ('EditarCadastros'), ('ExcluirCadastros')) AS p(permissao)
                WHERE u.perfil = 'Administrador'
                ON CONFLICT (usuario_id, permissao) DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_logs_auditoria_negocio_autor_usuario_id",
                table: "logs_auditoria_negocio",
                column: "autor_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_logs_auditoria_negocio_entidade_entidade_id",
                table: "logs_auditoria_negocio",
                columns: new[] { "entidade", "entidade_id" });

            migrationBuilder.CreateIndex(
                name: "ix_logs_auditoria_negocio_negocio_id_criado_em",
                table: "logs_auditoria_negocio",
                columns: new[] { "negocio_id", "criado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "logs_auditoria_negocio");

            migrationBuilder.DropIndex(
                name: "ix_usuarios_email",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "excluido",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "excluido_em",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "excluido",
                table: "servicos");

            migrationBuilder.DropColumn(
                name: "excluido_em",
                table: "servicos");

            migrationBuilder.DropColumn(
                name: "excluido",
                table: "profissionais");

            migrationBuilder.DropColumn(
                name: "excluido_em",
                table: "profissionais");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true);
        }
    }
}
