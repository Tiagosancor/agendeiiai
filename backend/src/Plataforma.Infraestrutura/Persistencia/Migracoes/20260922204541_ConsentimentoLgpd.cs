using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ConsentimentoLgpd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consentimento_data",
                table: "agendamentos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consentimento_ip",
                table: "agendamentos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "consentimento_versao_termos",
                table: "agendamentos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consentimento_data",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "consentimento_ip",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "consentimento_versao_termos",
                table: "agendamentos");
        }
    }
}
