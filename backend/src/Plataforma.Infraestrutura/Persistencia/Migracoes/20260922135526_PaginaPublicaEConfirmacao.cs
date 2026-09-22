using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class PaginaPublicaEConfirmacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "funcao",
                table: "profissionais",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email_contato",
                table: "negocios",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "whats_app_ativo_para_confirmacoes",
                table: "negocios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "cliente_id",
                table: "agendamentos",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "cupom_id",
                table: "agendamentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "desconto_aplicado",
                table: "agendamentos",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "codigos_verificacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hash_codigo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tentativas_restantes = table.Column<int>(type: "integer", nullable: false),
                    usado = table.Column<bool>(type: "boolean", nullable: false),
                    invalidado = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigos_verificacao", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    valido_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    limite_usos = table.Column<int>(type: "integer", nullable: true),
                    usos_atuais = table.Column<int>(type: "integer", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    servico_ids_escopo = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cupons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mensagens_contato",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    mensagem = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mensagens_contato", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_codigos_verificacao_negocio_id_telefone_criado_em",
                table: "codigos_verificacao",
                columns: new[] { "negocio_id", "telefone", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_cupons_negocio_id_codigo",
                table: "cupons",
                columns: new[] { "negocio_id", "codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "codigos_verificacao");

            migrationBuilder.DropTable(
                name: "cupons");

            migrationBuilder.DropTable(
                name: "mensagens_contato");

            migrationBuilder.DropColumn(
                name: "funcao",
                table: "profissionais");

            migrationBuilder.DropColumn(
                name: "email_contato",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "whats_app_ativo_para_confirmacoes",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "cupom_id",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "desconto_aplicado",
                table: "agendamentos");

            migrationBuilder.AlterColumn<Guid>(
                name: "cliente_id",
                table: "agendamentos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
