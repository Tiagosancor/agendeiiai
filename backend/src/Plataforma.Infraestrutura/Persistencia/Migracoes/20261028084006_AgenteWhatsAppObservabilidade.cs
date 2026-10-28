using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AgenteWhatsAppObservabilidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "falha_tipo",
                table: "usos_ia",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "iteracao",
                table: "usos_ia",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "latencia_ms",
                table: "usos_ia",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_fim",
                table: "usos_ia",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rodada_id",
                table: "usos_ia",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status_http",
                table: "usos_ia",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rodadas_agente",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    iniciada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duracao_ms = table.Column<int>(type: "integer", nullable: false),
                    desfecho = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    motivo_silencio = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    chamadas = table.Column<int>(type: "integer", nullable: false),
                    tokens_entrada = table.Column<int>(type: "integer", nullable: false),
                    tokens_saida = table.Column<int>(type: "integer", nullable: false),
                    modelo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    motivo_fim = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ferramentas = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    erros_ferramenta = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    falha_provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    prompt_versao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rodadas_agente", x => x.id);
                    table.ForeignKey(
                        name: "fk_rodadas_agente_conversas_whatsapp_conversa_id",
                        column: x => x.conversa_id,
                        principalTable: "conversas_whatsapp",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rodadas_agente_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rodadas_agente_conversa_id",
                table: "rodadas_agente",
                column: "conversa_id");

            migrationBuilder.CreateIndex(
                name: "ix_rodadas_agente_negocio_id_iniciada_em",
                table: "rodadas_agente",
                columns: new[] { "negocio_id", "iniciada_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rodadas_agente");

            migrationBuilder.DropColumn(
                name: "falha_tipo",
                table: "usos_ia");

            migrationBuilder.DropColumn(
                name: "iteracao",
                table: "usos_ia");

            migrationBuilder.DropColumn(
                name: "latencia_ms",
                table: "usos_ia");

            migrationBuilder.DropColumn(
                name: "motivo_fim",
                table: "usos_ia");

            migrationBuilder.DropColumn(
                name: "rodada_id",
                table: "usos_ia");

            migrationBuilder.DropColumn(
                name: "status_http",
                table: "usos_ia");
        }
    }
}
