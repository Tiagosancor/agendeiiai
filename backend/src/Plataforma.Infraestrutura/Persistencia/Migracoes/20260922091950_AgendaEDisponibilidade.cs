using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AgendaEDisponibilidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agendamentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    observacoes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    nome_informado = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reservado_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agendamentos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bloqueios_agenda",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fim_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bloqueios_agenda", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "horarios_trabalho",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia_semana = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    inicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    fim = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_horarios_trabalho", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "profissional_servicos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    preco_personalizado = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    duracao_personalizada_minutos = table.Column<int>(type: "integer", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profissional_servicos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agendamento_servicos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agendamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    preco = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    duracao_minutos = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agendamento_servicos", x => x.id);
                    table.ForeignKey(
                        name: "fk_agendamento_servicos_agendamentos_agendamento_id",
                        column: x => x.agendamento_id,
                        principalTable: "agendamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Exclusion constraint (seção 8.2.1) — a garantia REAL contra sobreposição.
            // Mesmo com bug na aplicação ou requisições paralelas, o banco recusa a
            // sobreposição do mesmo profissional em dois agendamentos ativos
            // (Reservado/Agendado/Concluido). Precisa do btree_gist (Sprint 0) pro "=" do
            // profissional_id funcionar dentro do índice GiST junto com o "&&" do intervalo.
            migrationBuilder.Sql("""
                ALTER TABLE agendamentos ADD CONSTRAINT ex_agendamentos_sem_sobreposicao
                EXCLUDE USING gist (profissional_id WITH =, tstzrange(inicio, fim) WITH &&)
                WHERE (status IN ('Reservado', 'Agendado', 'Concluido'));
                """);

            migrationBuilder.CreateIndex(
                name: "ix_agendamento_servicos_agendamento_id",
                table: "agendamento_servicos",
                column: "agendamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_agendamentos_profissional_id_inicio",
                table: "agendamentos",
                columns: new[] { "profissional_id", "inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_bloqueios_agenda_profissional_id_inicio_utc_fim_utc",
                table: "bloqueios_agenda",
                columns: new[] { "profissional_id", "inicio_utc", "fim_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_horarios_trabalho_profissional_id_dia_semana",
                table: "horarios_trabalho",
                columns: new[] { "profissional_id", "dia_semana" });

            migrationBuilder.CreateIndex(
                name: "ix_profissional_servicos_profissional_id_servico_id",
                table: "profissional_servicos",
                columns: new[] { "profissional_id", "servico_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agendamento_servicos");

            migrationBuilder.DropTable(
                name: "bloqueios_agenda");

            migrationBuilder.DropTable(
                name: "horarios_trabalho");

            migrationBuilder.DropTable(
                name: "profissional_servicos");

            migrationBuilder.DropTable(
                name: "agendamentos");
        }
    }
}
