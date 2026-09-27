using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class LembreteUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sem DropColumn de lembrete24h_enviado: a versão anterior da API continua no ar entre
            // esta migration (manual, no Neon) e o deploy, e ainda lê essa coluna. Fica órfã (default false).
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "horario_combinado_em",
                table: "agendamentos",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "horario_combinado_em",
                table: "agendamentos");

        }
    }
}
