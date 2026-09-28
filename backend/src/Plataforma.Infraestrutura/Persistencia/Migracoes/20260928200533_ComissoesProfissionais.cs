using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ComissoesProfissionais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "percentual_comissao",
                table: "profissionais",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "comissao_calculada_em",
                table: "agendamento_servicos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "comissao_percentual",
                table: "agendamento_servicos",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "comissao_profissional_id",
                table: "agendamento_servicos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "comissao_valor",
                table: "agendamento_servicos",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "comissao_valor_base",
                table: "agendamento_servicos",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_profissionais_percentual_comissao",
                table: "profissionais",
                sql: "percentual_comissao >= 0 AND percentual_comissao <= 100");

            migrationBuilder.CreateIndex(
                name: "ix_agendamento_servicos_comissao_profissional_id",
                table: "agendamento_servicos",
                column: "comissao_profissional_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_profissionais_percentual_comissao",
                table: "profissionais");

            migrationBuilder.DropIndex(
                name: "ix_agendamento_servicos_comissao_profissional_id",
                table: "agendamento_servicos");

            migrationBuilder.DropColumn(
                name: "percentual_comissao",
                table: "profissionais");

            migrationBuilder.DropColumn(
                name: "comissao_calculada_em",
                table: "agendamento_servicos");

            migrationBuilder.DropColumn(
                name: "comissao_percentual",
                table: "agendamento_servicos");

            migrationBuilder.DropColumn(
                name: "comissao_profissional_id",
                table: "agendamento_servicos");

            migrationBuilder.DropColumn(
                name: "comissao_valor",
                table: "agendamento_servicos");

            migrationBuilder.DropColumn(
                name: "comissao_valor_base",
                table: "agendamento_servicos");
        }
    }
}
