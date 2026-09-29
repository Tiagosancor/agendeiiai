using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ForcarAgendamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "forcado_em",
                table: "agendamentos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "forcado_motivo",
                table: "agendamentos",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "forcado_por_usuario_id",
                table: "agendamentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "forcado_regras",
                table: "agendamentos",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "forcado_em",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "forcado_motivo",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "forcado_por_usuario_id",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "forcado_regras",
                table: "agendamentos");
        }
    }
}
