using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class FidelidadeELgpd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "excluido",
                table: "clientes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "excluido_em",
                table: "clientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "programas_fidelidade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selos_necessarios = table.Column<int>(type: "integer", nullable: false),
                    descricao_recompensa = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_programas_fidelidade", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "selos_cliente",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agendamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resgatado = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_selos_cliente", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_programas_fidelidade_negocio_id",
                table: "programas_fidelidade",
                column: "negocio_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_selos_cliente_agendamento_id",
                table: "selos_cliente",
                column: "agendamento_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_selos_cliente_cliente_id_resgatado",
                table: "selos_cliente",
                columns: new[] { "cliente_id", "resgatado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "programas_fidelidade");

            migrationBuilder.DropTable(
                name: "selos_cliente");

            migrationBuilder.DropColumn(
                name: "excluido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "excluido_em",
                table: "clientes");
        }
    }
}
