using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AgenteWhatsAppInfra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instancias_whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_instancia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    agente_ativo = table.Column<bool>(type: "boolean", nullable: false),
                    mensagem_boas_vindas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    horario_atendimento_json = table.Column<string>(type: "jsonb", nullable: true),
                    mensagem_transferencia = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instancias_whatsapp", x => x.id);
                    table.ForeignKey(
                        name: "fk_instancias_whatsapp_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversas_whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instancia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telefone_cliente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ultima_mensagem_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resumo = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversas_whatsapp", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversas_whatsapp_instancias_whats_app_instancia_id",
                        column: x => x.instancia_id,
                        principalTable: "instancias_whatsapp",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mensagens_whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instancia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_externo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    papel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    conteudo = table.Column<string>(type: "text", nullable: false),
                    nome_ferramenta = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mensagens_whatsapp", x => x.id);
                    table.ForeignKey(
                        name: "fk_mensagens_whatsapp_conversas_whatsapp_conversa_id",
                        column: x => x.conversa_id,
                        principalTable: "conversas_whatsapp",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversas_whatsapp_instancia_id_telefone_cliente",
                table: "conversas_whatsapp",
                columns: new[] { "instancia_id", "telefone_cliente" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversas_whatsapp_negocio_id_ultima_mensagem_em",
                table: "conversas_whatsapp",
                columns: new[] { "negocio_id", "ultima_mensagem_em" });

            migrationBuilder.CreateIndex(
                name: "ix_instancias_whatsapp_negocio_id",
                table: "instancias_whatsapp",
                column: "negocio_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instancias_whatsapp_nome_instancia",
                table: "instancias_whatsapp",
                column: "nome_instancia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mensagens_whatsapp_conversa_id_criado_em",
                table: "mensagens_whatsapp",
                columns: new[] { "conversa_id", "criado_em" });

            migrationBuilder.CreateIndex(
                name: "ix_mensagens_whatsapp_instancia_id_id_externo",
                table: "mensagens_whatsapp",
                columns: new[] { "instancia_id", "id_externo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mensagens_whatsapp");

            migrationBuilder.DropTable(
                name: "conversas_whatsapp");

            migrationBuilder.DropTable(
                name: "instancias_whatsapp");
        }
    }
}
