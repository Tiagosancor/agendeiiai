using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class CadastroNegocio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "checklist_dispensado",
                table: "negocios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "link_agendamento_copiado",
                table: "negocios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Negócios que já existiam não estão no "primeiro acesso" (seção 6.5): sem checklist.
            migrationBuilder.Sql("UPDATE negocios SET checklist_dispensado = true;");

            migrationBuilder.CreateTable(
                name: "chaves_idempotencia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    escopo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    chave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chaves_idempotencia", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "codigos_cadastro",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    hash_codigo = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tentativas_restantes = table.Column<int>(type: "integer", nullable: false),
                    usado = table.Column<bool>(type: "boolean", nullable: false),
                    invalidado = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigos_cadastro", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registros_teste_gratis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registros_teste_gratis", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chaves_idempotencia_escopo_chave",
                table: "chaves_idempotencia",
                columns: new[] { "escopo", "chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_codigos_cadastro_email_criado_em",
                table: "codigos_cadastro",
                columns: new[] { "email", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_registros_teste_gratis_email",
                table: "registros_teste_gratis",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_registros_teste_gratis_telefone",
                table: "registros_teste_gratis",
                column: "telefone",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chaves_idempotencia");

            migrationBuilder.DropTable(
                name: "codigos_cadastro");

            migrationBuilder.DropTable(
                name: "registros_teste_gratis");

            migrationBuilder.DropColumn(
                name: "checklist_dispensado",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "link_agendamento_copiado",
                table: "negocios");
        }
    }
}
