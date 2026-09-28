using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class QuinzenasComissao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "acerto_por_quinzena",
                table: "profissionais",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "periodos_comissao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fim = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fechado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fechado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_periodos_comissao", x => x.id);
                    table.CheckConstraint("ck_periodos_comissao_inicio_antes_do_fim", "inicio <= fim");
                });

            migrationBuilder.CreateTable(
                name: "fechamentos_comissao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo_comissao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_cobrado = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    total_comissao = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    quantidade_servicos = table.Column<int>(type: "integer", nullable: false),
                    fechado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fechado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fechamentos_comissao", x => x.id);
                    table.ForeignKey(
                        name: "fk_fechamentos_comissao_periodos_comissao_periodo_comissao_id",
                        column: x => x.periodo_comissao_id,
                        principalTable: "periodos_comissao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fechamentos_comissao_periodo_comissao_id_profissional_id",
                table: "fechamentos_comissao",
                columns: new[] { "periodo_comissao_id", "profissional_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fechamentos_comissao_profissional_id",
                table: "fechamentos_comissao",
                column: "profissional_id");

            migrationBuilder.CreateIndex(
                name: "ix_periodos_comissao_negocio_id_inicio",
                table: "periodos_comissao",
                columns: new[] { "negocio_id", "inicio" });

            // Seção 7: quinzenas do mesmo negócio não se sobrepõem — garantido no banco, com a mesma
            // técnica da agenda (seção 8.2.1). Datas são dias inteiros: intervalo fechado nas duas pontas.
            migrationBuilder.Sql("""
                ALTER TABLE periodos_comissao ADD CONSTRAINT ex_periodos_comissao_sem_sobreposicao
                EXCLUDE USING gist (negocio_id WITH =, daterange(inicio, fim, '[]') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fechamentos_comissao");

            migrationBuilder.DropTable(
                name: "periodos_comissao");

            migrationBuilder.DropColumn(
                name: "acerto_por_quinzena",
                table: "profissionais");
        }
    }
}
