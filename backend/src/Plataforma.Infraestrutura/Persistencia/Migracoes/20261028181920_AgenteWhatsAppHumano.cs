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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "atendente_avisado_em",
                table: "conversas_whatsapp");

            migrationBuilder.DropColumn(
                name: "devolvida_em",
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
