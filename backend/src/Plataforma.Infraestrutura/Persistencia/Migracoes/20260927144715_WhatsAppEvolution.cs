using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class WhatsAppEvolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "whats_app_aviso_profissional",
                table: "negocios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "canal_email_status",
                table: "codigos_verificacao",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "canal_whats_app_status",
                table: "codigos_verificacao",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "id_mensagem_whats_app",
                table: "codigos_verificacao",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tentativas_reenvio",
                table: "codigos_verificacao",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "eventos_webhook_whatsapp",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    id_evento = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    id_mensagem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_webhook_whatsapp", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notificacoes_profissional",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agendamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    canal_email_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    canal_whats_app_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    id_mensagem_whats_app = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificacoes_profissional", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_codigos_verificacao_id_mensagem_whats_app",
                table: "codigos_verificacao",
                column: "id_mensagem_whats_app");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_webhook_whatsapp_id_mensagem",
                table: "eventos_webhook_whatsapp",
                column: "id_mensagem");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_webhook_whatsapp_provedor_id_evento",
                table: "eventos_webhook_whatsapp",
                columns: new[] { "provedor", "id_evento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_profissional_agendamento_id",
                table: "notificacoes_profissional",
                column: "agendamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_profissional_id_mensagem_whats_app",
                table: "notificacoes_profissional",
                column: "id_mensagem_whats_app");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eventos_webhook_whatsapp");

            migrationBuilder.DropTable(
                name: "notificacoes_profissional");

            migrationBuilder.DropIndex(
                name: "ix_codigos_verificacao_id_mensagem_whats_app",
                table: "codigos_verificacao");

            migrationBuilder.DropColumn(
                name: "whats_app_aviso_profissional",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "canal_email_status",
                table: "codigos_verificacao");

            migrationBuilder.DropColumn(
                name: "canal_whats_app_status",
                table: "codigos_verificacao");

            migrationBuilder.DropColumn(
                name: "id_mensagem_whats_app",
                table: "codigos_verificacao");

            migrationBuilder.DropColumn(
                name: "tentativas_reenvio",
                table: "codigos_verificacao");
        }
    }
}
