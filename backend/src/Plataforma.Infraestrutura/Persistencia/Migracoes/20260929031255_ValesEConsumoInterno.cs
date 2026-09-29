using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ValesEConsumoInterno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "liquido",
                table: "fechamentos_comissao",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            // Fechamentos anteriores ao recurso não tinham desconto: o líquido é a própria comissão.
            migrationBuilder.Sql("UPDATE fechamentos_comissao SET liquido = total_comissao;");

            migrationBuilder.AddColumn<decimal>(
                name: "saldo_restante",
                table: "fechamentos_comissao",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_comissao_produtos",
                table: "fechamentos_comissao",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_consumo",
                table: "fechamentos_comissao",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "total_vales",
                table: "fechamentos_comissao",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "lancamentos_saldo_devedor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    data = table.Column<DateOnly>(type: "date", nullable: false),
                    descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    produto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nome_produto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    quantidade = table.Column<int>(type: "integer", nullable: true),
                    valor_unitario = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    movimento_estoque_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lancado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lancamentos_saldo_devedor", x => x.id);
                    table.CheckConstraint("ck_lancamentos_saldo_devedor_valor", "valor >= 0");
                });

            migrationBuilder.CreateTable(
                name: "quitacoes_saldo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fechamento_comissao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lancamento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quitacoes_saldo", x => x.id);
                    table.CheckConstraint("ck_quitacoes_saldo_valor", "valor > 0");
                    table.ForeignKey(
                        name: "fk_quitacoes_saldo_fechamentos_comissao_fechamento_comissao_id",
                        column: x => x.fechamento_comissao_id,
                        principalTable: "fechamentos_comissao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_quitacoes_saldo_lancamentos_saldo_devedor_lancamento_id",
                        column: x => x.lancamento_id,
                        principalTable: "lancamentos_saldo_devedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_saldo_devedor_profissional_id_data",
                table: "lancamentos_saldo_devedor",
                columns: new[] { "profissional_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_quitacoes_saldo_fechamento_comissao_id",
                table: "quitacoes_saldo",
                column: "fechamento_comissao_id");

            migrationBuilder.CreateIndex(
                name: "ix_quitacoes_saldo_lancamento_id",
                table: "quitacoes_saldo",
                column: "lancamento_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quitacoes_saldo");

            migrationBuilder.DropTable(
                name: "lancamentos_saldo_devedor");

            migrationBuilder.DropColumn(
                name: "liquido",
                table: "fechamentos_comissao");

            migrationBuilder.DropColumn(
                name: "saldo_restante",
                table: "fechamentos_comissao");

            migrationBuilder.DropColumn(
                name: "total_comissao_produtos",
                table: "fechamentos_comissao");

            migrationBuilder.DropColumn(
                name: "total_consumo",
                table: "fechamentos_comissao");

            migrationBuilder.DropColumn(
                name: "total_vales",
                table: "fechamentos_comissao");
        }
    }
}
