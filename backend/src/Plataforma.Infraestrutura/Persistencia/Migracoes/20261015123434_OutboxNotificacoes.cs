using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class OutboxNotificacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notificacoes_pendentes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    chave_deduplicacao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    proxima_tentativa_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reivindicacao_id = table.Column<Guid>(type: "uuid", nullable: true),
                    envio_iniciado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    enviada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_erro_codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    id_mensagem_provedor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    agendamento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    versao_horario = table.Column<long>(type: "bigint", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificacoes_pendentes", x => x.id);
                    table.CheckConstraint("ck_notificacoes_pendentes_lease", "(status = 'Enviando' AND lease_ate IS NOT NULL AND reivindicacao_id IS NOT NULL) OR (status <> 'Enviando' AND reivindicacao_id IS NULL)");
                    table.CheckConstraint("ck_notificacoes_pendentes_tentativas", "tentativas >= 0");
                    table.ForeignKey(
                        name: "fk_notificacoes_pendentes_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_pendentes_agendamento",
                table: "notificacoes_pendentes",
                column: "agendamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_pendentes_id_mensagem",
                table: "notificacoes_pendentes",
                column: "id_mensagem_provedor",
                filter: "id_mensagem_provedor IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_pendentes_lease",
                table: "notificacoes_pendentes",
                column: "lease_ate",
                filter: "status = 'Enviando'");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_pendentes_prontas",
                table: "notificacoes_pendentes",
                columns: new[] { "proxima_tentativa_em", "id" },
                filter: "status = 'Pendente'");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_pendentes_status_atualizada",
                table: "notificacoes_pendentes",
                columns: new[] { "status", "atualizado_em" });

            migrationBuilder.CreateIndex(
                name: "ux_notificacoes_pendentes_dedup",
                table: "notificacoes_pendentes",
                columns: new[] { "negocio_id", "chave_deduplicacao", "canal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notificacoes_pendentes");
        }
    }
}
