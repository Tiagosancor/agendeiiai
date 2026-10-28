using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AgenteIaServicoPremium : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "agente_ia_alterado_em",
                table: "negocios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "agente_ia_alterado_por",
                table: "negocios",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "agente_ia_contratado",
                table: "negocios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "agente_ia_origem",
                table: "negocios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_negocios_agente_ia_origem",
                table: "negocios",
                sql: "agente_ia_origem IS NULL OR agente_ia_origem IN ('Manual', 'Plano', 'Adicional')");

            migrationBuilder.CreateIndex(
                name: "ix_mensagens_whatsapp_indisponivel_aviso",
                table: "mensagens_whatsapp",
                columns: new[] { "negocio_id", "criado_em" },
                filter: "nome_ferramenta = 'indisponivel_aviso'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_negocios_agente_ia_origem",
                table: "negocios");

            migrationBuilder.DropIndex(
                name: "ix_mensagens_whatsapp_indisponivel_aviso",
                table: "mensagens_whatsapp");

            migrationBuilder.DropColumn(
                name: "agente_ia_alterado_em",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "agente_ia_alterado_por",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "agente_ia_contratado",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "agente_ia_origem",
                table: "negocios");
        }
    }
}
