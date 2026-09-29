using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class CobrancaAsaas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "corpo",
                table: "eventos_webhook_pagamento",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resultado",
                table: "eventos_webhook_pagamento",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "estornada_em",
                table: "cobrancas_assinatura",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelamento_pedido_em",
                table: "assinaturas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "documento_titular_mascarado",
                table: "assinaturas",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "id_cliente_gateway",
                table: "assinaturas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "corpo",
                table: "eventos_webhook_pagamento");

            migrationBuilder.DropColumn(
                name: "resultado",
                table: "eventos_webhook_pagamento");

            migrationBuilder.DropColumn(
                name: "estornada_em",
                table: "cobrancas_assinatura");

            migrationBuilder.DropColumn(
                name: "cancelamento_pedido_em",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "documento_titular_mascarado",
                table: "assinaturas");

            migrationBuilder.DropColumn(
                name: "id_cliente_gateway",
                table: "assinaturas");
        }
    }
}
