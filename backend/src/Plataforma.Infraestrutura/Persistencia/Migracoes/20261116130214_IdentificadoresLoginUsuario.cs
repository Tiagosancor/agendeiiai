using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class IdentificadoresLoginUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_usuarios_email",
                table: "usuarios");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "usuarios",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_verificado_em",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nome_usuario",
                table: "usuarios",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "senha_definida_por_terceiro",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "telefone_login",
                table: "usuarios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // E-mails que já existem contam como verificados (decisão do dono): o dono passou pelo código do cadastro e quem
            // foi criado pelo administrador já recebe a redefinição de senha hoje. Só os não excluídos com e-mail.
            // Nenhuma linha vira NULL aqui: a versão anterior do código continua lendo "email" como não anulável.
            migrationBuilder.Sql(
                "UPDATE usuarios SET email_verificado_em = now() WHERE excluido = false AND email IS NOT NULL AND email <> '';");

            migrationBuilder.AddColumn<string>(
                name: "email_destino_hash",
                table: "redefinicoes_senha",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo",
                table: "redefinicoes_senha",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Redefinicao");

            migrationBuilder.CreateTable(
                name: "tentativas_login",
                columns: table => new
                {
                    chave = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    janela_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    falhas = table.Column<int>(type: "integer", nullable: false),
                    bloqueado_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tentativas_login", x => x.chave);
                });

            migrationBuilder.CreateTable(
                name: "verificacoes_email_usuario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    codigo_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    maximo_tentativas = table.Column<int>(type: "integer", nullable: false),
                    usado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invalidado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verificacoes_email_usuario", x => x.id);
                    table.ForeignKey(
                        name: "fk_verificacoes_email_usuario_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true,
                filter: "excluido = false AND email IS NOT NULL AND email <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_nome_usuario",
                table: "usuarios",
                column: "nome_usuario",
                unique: true,
                filter: "excluido = false AND nome_usuario IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_telefone_login",
                table: "usuarios",
                column: "telefone_login",
                unique: true,
                filter: "excluido = false AND telefone_login IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuarios_algum_identificador",
                table: "usuarios",
                sql: "excluido OR coalesce(email, '') <> '' OR nome_usuario IS NOT NULL OR telefone_login IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuarios_nome_usuario_formato",
                table: "usuarios",
                sql: "nome_usuario IS NULL OR (nome_usuario ~ '^[a-z0-9][a-z0-9._-]{1,28}[a-z0-9]$' AND nome_usuario ~ '[a-z]' AND nome_usuario !~ '[._-]{2}')");

            migrationBuilder.CreateIndex(
                name: "ix_tentativas_login_atualizado_em",
                table: "tentativas_login",
                column: "atualizado_em");

            migrationBuilder.CreateIndex(
                name: "ix_verificacoes_email_usuario_usuario_id_criado_em",
                table: "verificacoes_email_usuario",
                columns: new[] { "usuario_id", "criado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tentativas_login");

            migrationBuilder.DropTable(
                name: "verificacoes_email_usuario");

            migrationBuilder.DropIndex(
                name: "ix_usuarios_email",
                table: "usuarios");

            migrationBuilder.DropIndex(
                name: "ix_usuarios_nome_usuario",
                table: "usuarios");

            migrationBuilder.DropIndex(
                name: "ix_usuarios_telefone_login",
                table: "usuarios");

            migrationBuilder.DropCheckConstraint(
                name: "ck_usuarios_algum_identificador",
                table: "usuarios");

            migrationBuilder.DropCheckConstraint(
                name: "ck_usuarios_nome_usuario_formato",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "email_verificado_em",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "nome_usuario",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "senha_definida_por_terceiro",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "telefone_login",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "email_destino_hash",
                table: "redefinicoes_senha");

            migrationBuilder.DropColumn(
                name: "tipo",
                table: "redefinicoes_senha");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "usuarios",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true,
                filter: "excluido = false");
        }
    }
}
