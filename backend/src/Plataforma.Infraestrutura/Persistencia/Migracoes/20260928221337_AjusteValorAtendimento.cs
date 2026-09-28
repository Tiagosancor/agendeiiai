using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AjusteValorAtendimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "forcado",
                table: "agendamentos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "preco_ajustado",
                table: "agendamento_servicos",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ajustes_valor_atendimento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agendamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agendamento_servico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    modo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_informado = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    preco_original = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    valor_antes = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    valor_depois = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    apos_conclusao = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ajustes_valor_atendimento", x => x.id);
                    table.ForeignKey(
                        name: "fk_ajustes_valor_atendimento_agendamentos_agendamento_id",
                        column: x => x.agendamento_id,
                        principalTable: "agendamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ajustes_valor_atendimento_agendamento_id",
                table: "ajustes_valor_atendimento",
                column: "agendamento_id");

            // Migration crítica (seção 8.2 + itens 8 e 10 da seção 14): a exclusion constraint é
            // recriada UMA vez com as duas mudanças — EmAtendimento passa a ocupar o horário (sem isso,
            // o horário de quem está sendo atendido ficaria livre para outro agendamento) e o agendamento
            // forçado (item 10, coluna já criada acima, sempre falsa até lá) fica fora da restrição, com a
            // conferência feita na aplicação. Drop e add na mesma transação da migration.
            migrationBuilder.Sql("""
                ALTER TABLE agendamentos DROP CONSTRAINT ex_agendamentos_sem_sobreposicao;
                ALTER TABLE agendamentos ADD CONSTRAINT ex_agendamentos_sem_sobreposicao
                EXCLUDE USING gist (profissional_id WITH =, tstzrange(inicio, fim) WITH &&)
                WHERE (status IN ('Reservado', 'Agendado', 'EmAtendimento', 'Concluido') AND NOT forcado);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE agendamentos DROP CONSTRAINT ex_agendamentos_sem_sobreposicao;
                ALTER TABLE agendamentos ADD CONSTRAINT ex_agendamentos_sem_sobreposicao
                EXCLUDE USING gist (profissional_id WITH =, tstzrange(inicio, fim) WITH &&)
                WHERE (status IN ('Reservado', 'Agendado', 'Concluido'));
                """);

            migrationBuilder.DropTable(
                name: "ajustes_valor_atendimento");

            migrationBuilder.DropColumn(
                name: "forcado",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "preco_ajustado",
                table: "agendamento_servicos");
        }
    }
}
