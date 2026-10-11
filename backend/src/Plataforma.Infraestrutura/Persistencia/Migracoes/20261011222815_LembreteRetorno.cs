using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class LembreteRetorno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lembrete_retorno_ativado_em",
                table: "negocios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "lembrete_retorno_ativo",
                table: "negocios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "lembrete_retorno_dias",
                table: "negocios",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            // Os padrões ficam na coluna (não no modelo): negócios e clientes que já existem — e os criados pela versão anterior
            // enquanto ela ainda estiver no ar — entram com o lembrete desligado, 30 dias, 10:00, a mensagem padrão e aceitando.
            migrationBuilder.AddColumn<TimeOnly>(
                name: "lembrete_retorno_horario",
                table: "negocios",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(10, 0, 0));

            migrationBuilder.AddColumn<string>(
                name: "lembrete_retorno_mensagem",
                table: "negocios",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "Olá, {nome_cliente}! Já faz {dias} dias desde seu último atendimento na {nome_empresa}. Que tal agendar o próximo? {link_agendamento}");

            migrationBuilder.AddColumn<bool>(
                name: "aceita_lembrete_retorno",
                table: "clientes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "lembretes_retorno_envios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agendamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia_envio = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    enviado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    id_mensagem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    agendamento_gerado_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lembretes_retorno_envios", x => x.id);
                    table.ForeignKey(
                        name: "fk_lembretes_retorno_envios_agendamentos_agendamento_id",
                        column: x => x.agendamento_id,
                        principalTable: "agendamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lembretes_retorno_envios_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lembretes_retorno_envios_agendamento_id",
                table: "lembretes_retorno_envios",
                column: "agendamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_lembretes_retorno_envios_cliente_id_agendamento_id",
                table: "lembretes_retorno_envios",
                columns: new[] { "cliente_id", "agendamento_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lembretes_retorno_envios_negocio_id_enviado_em",
                table: "lembretes_retorno_envios",
                columns: new[] { "negocio_id", "enviado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lembretes_retorno_envios");

            migrationBuilder.DropColumn(
                name: "lembrete_retorno_ativado_em",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "lembrete_retorno_ativo",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "lembrete_retorno_dias",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "lembrete_retorno_horario",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "lembrete_retorno_mensagem",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "aceita_lembrete_retorno",
                table: "clientes");
        }
    }
}
