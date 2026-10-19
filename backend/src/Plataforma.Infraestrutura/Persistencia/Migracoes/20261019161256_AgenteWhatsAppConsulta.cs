using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AgenteWhatsAppConsulta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processada_ate",
                table: "conversas_whatsapp",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "processando_ate",
                table: "conversas_whatsapp",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "usos_ia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modelo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tokens_entrada = table.Column<int>(type: "integer", nullable: false),
                    tokens_saida = table.Column<int>(type: "integer", nullable: false),
                    sucesso = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usos_ia", x => x.id);
                    table.ForeignKey(
                        name: "fk_usos_ia_conversas_whatsapp_conversa_id",
                        column: x => x.conversa_id,
                        principalTable: "conversas_whatsapp",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_usos_ia_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_usos_ia_conversa_id",
                table: "usos_ia",
                column: "conversa_id");

            migrationBuilder.CreateIndex(
                name: "ix_usos_ia_negocio_id_criado_em",
                table: "usos_ia",
                columns: new[] { "negocio_id", "criado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usos_ia");

            migrationBuilder.DropColumn(
                name: "processada_ate",
                table: "conversas_whatsapp");

            migrationBuilder.DropColumn(
                name: "processando_ate",
                table: "conversas_whatsapp");
        }
    }
}
