using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AgenteWhatsAppAgendamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "aviso_automatizado_em",
                table: "conversas_whatsapp",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pendencias_agendamento_whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agendamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resumo_mensagem_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resumo_enviado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmacao_mensagem_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concluida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmacao_operacao_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nome_informado = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    versao_termos = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pendencias_agendamento_whatsapp", x => x.id);
                    table.ForeignKey(
                        name: "fk_pendencias_agendamento_whatsapp_agendamentos_agendamento_id",
                        column: x => x.agendamento_id,
                        principalTable: "agendamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pendencias_agendamento_whatsapp_conversas_whatsapp_conversa",
                        column: x => x.conversa_id,
                        principalTable: "conversas_whatsapp",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pendencias_agendamento_whatsapp_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pendencias_agendamento_whatsapp_agendamento_id",
                table: "pendencias_agendamento_whatsapp",
                column: "agendamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_pendencias_agendamento_whatsapp_conversa_id",
                table: "pendencias_agendamento_whatsapp",
                column: "conversa_id",
                unique: true,
                filter: "status = 'Aguardando'");

            migrationBuilder.CreateIndex(
                name: "ix_pendencias_agendamento_whatsapp_expira_em_negocio_id",
                table: "pendencias_agendamento_whatsapp",
                columns: new[] { "expira_em", "negocio_id" },
                filter: "status = 'Aguardando'");

            migrationBuilder.CreateIndex(
                name: "ix_pendencias_agendamento_whatsapp_negocio_id_criado_em",
                table: "pendencias_agendamento_whatsapp",
                columns: new[] { "negocio_id", "criado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pendencias_agendamento_whatsapp");

            migrationBuilder.DropColumn(
                name: "aviso_automatizado_em",
                table: "conversas_whatsapp");
        }
    }
}
