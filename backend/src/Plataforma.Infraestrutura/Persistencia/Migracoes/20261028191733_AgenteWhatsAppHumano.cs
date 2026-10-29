using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AgenteWhatsAppHumano : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "atendente_avisado_em",
                table: "conversas_whatsapp",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "devolvida_em",
                table: "conversas_whatsapp",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "epoca_atendimento",
                table: "conversas_whatsapp",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "humano_ate",
                table: "conversas_whatsapp",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "humano_desde",
                table: "conversas_whatsapp",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "humano_motivo",
                table: "conversas_whatsapp",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "humano_por_usuario_id",
                table: "conversas_whatsapp",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "avisos_atendente_whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos_atendente_whatsapp", x => x.id);
                    table.ForeignKey(
                        name: "fk_avisos_atendente_whatsapp_conversas_whats_app_conversa_id",
                        column: x => x.conversa_id,
                        principalTable: "conversas_whatsapp",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_avisos_atendente_whatsapp_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_atendente_whatsapp_conversa_id",
                table: "avisos_atendente_whatsapp",
                column: "conversa_id");

            migrationBuilder.CreateIndex(
                name: "ix_avisos_atendente_whatsapp_negocio_id_em",
                table: "avisos_atendente_whatsapp",
                columns: new[] { "negocio_id", "em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos_atendente_whatsapp");

            migrationBuilder.DropColumn(
                name: "atendente_avisado_em",
                table: "conversas_whatsapp");

            migrationBuilder.DropColumn(
                name: "devolvida_em",
                table: "conversas_whatsapp");

            migrationBuilder.DropColumn(
                name: "epoca_atendimento",
                table: "conversas_whatsapp");

            migrationBuilder.DropColumn(
                name: "humano_ate",
                table: "conversas_whatsapp");

            migrationBuilder.DropColumn(
                name: "humano_desde",
                table: "conversas_whatsapp");

            migrationBuilder.DropColumn(
                name: "humano_motivo",
                table: "conversas_whatsapp");

            migrationBuilder.DropColumn(
                name: "humano_por_usuario_id",
                table: "conversas_whatsapp");
        }
    }
}
