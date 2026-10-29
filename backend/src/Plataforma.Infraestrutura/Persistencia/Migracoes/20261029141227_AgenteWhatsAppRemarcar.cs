using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AgenteWhatsAppRemarcar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "duracao_minutos",
                table: "pendencias_gestao_whatsapp",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "inicio_anterior",
                table: "pendencias_gestao_whatsapp",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "profissional_id",
                table: "pendencias_gestao_whatsapp",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "duracao_minutos",
                table: "pendencias_gestao_whatsapp");

            migrationBuilder.DropColumn(
                name: "inicio_anterior",
                table: "pendencias_gestao_whatsapp");

            migrationBuilder.DropColumn(
                name: "profissional_id",
                table: "pendencias_gestao_whatsapp");
        }
    }
}
