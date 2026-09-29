using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class VendasProdutos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "percentual_comissao_produto_venda",
                table: "usuarios",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "percentual_comissao_produto_venda",
                table: "profissionais",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "vendas_produto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agendamento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendedor_profissional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendedor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vendedor_nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    total = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    percentual_comissao = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    valor_comissao = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    lancada_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendas_produto", x => x.id);
                    table.CheckConstraint("ck_vendas_produto_um_vendedor", "(vendedor_profissional_id IS NULL) <> (vendedor_usuario_id IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "itens_venda_produto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    produto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_produto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    valor_unitario = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    movimento_estoque_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itens_venda_produto", x => x.id);
                    table.ForeignKey(
                        name: "fk_itens_venda_produto_movimentos_estoque_movimento_estoque_id",
                        column: x => x.movimento_estoque_id,
                        principalTable: "movimentos_estoque",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_itens_venda_produto_produtos_produto_id",
                        column: x => x.produto_id,
                        principalTable: "produtos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_itens_venda_produto_vendas_produto_venda_id",
                        column: x => x.venda_id,
                        principalTable: "vendas_produto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuarios_percentual_comissao_produto_venda",
                table: "usuarios",
                sql: "percentual_comissao_produto_venda >= 0 AND percentual_comissao_produto_venda <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_profissionais_percentual_comissao_produto_venda",
                table: "profissionais",
                sql: "percentual_comissao_produto_venda >= 0 AND percentual_comissao_produto_venda <= 100");

            migrationBuilder.CreateIndex(
                name: "ix_itens_venda_produto_movimento_estoque_id",
                table: "itens_venda_produto",
                column: "movimento_estoque_id");

            migrationBuilder.CreateIndex(
                name: "ix_itens_venda_produto_produto_id",
                table: "itens_venda_produto",
                column: "produto_id");

            migrationBuilder.CreateIndex(
                name: "ix_itens_venda_produto_venda_id",
                table: "itens_venda_produto",
                column: "venda_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendas_produto_agendamento_id",
                table: "vendas_produto",
                column: "agendamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_vendas_produto_negocio_id_data",
                table: "vendas_produto",
                columns: new[] { "negocio_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_vendas_produto_vendedor_profissional_id_data",
                table: "vendas_produto",
                columns: new[] { "vendedor_profissional_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_vendas_produto_vendedor_usuario_id_data",
                table: "vendas_produto",
                columns: new[] { "vendedor_usuario_id", "data" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "itens_venda_produto");

            migrationBuilder.DropTable(
                name: "vendas_produto");

            migrationBuilder.DropCheckConstraint(
                name: "ck_usuarios_percentual_comissao_produto_venda",
                table: "usuarios");

            migrationBuilder.DropCheckConstraint(
                name: "ck_profissionais_percentual_comissao_produto_venda",
                table: "profissionais");

            migrationBuilder.DropColumn(
                name: "percentual_comissao_produto_venda",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "percentual_comissao_produto_venda",
                table: "profissionais");
        }
    }
}
