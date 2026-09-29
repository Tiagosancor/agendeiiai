using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class EstornoVenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "estornada_em",
                table: "vendas_produto",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "estornada_por_usuario_id",
                table: "vendas_produto",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_estorno",
                table: "vendas_produto",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "vendas_produto",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "estornada_em",
                table: "vendas_produto");

            migrationBuilder.DropColumn(
                name: "estornada_por_usuario_id",
                table: "vendas_produto");

            migrationBuilder.DropColumn(
                name: "motivo_estorno",
                table: "vendas_produto");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "vendas_produto");
        }
    }
}
