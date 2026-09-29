using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class EstoqueProdutos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "produtos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    categoria = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    preco_custo = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    preco_venda = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    quantidade_estoque = table.Column<int>(type: "integer", nullable: false),
                    quantidade_minima = table.Column<int>(type: "integer", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_produtos", x => x.id);
                    table.CheckConstraint("ck_produtos_quantidade_estoque_nao_negativa", "quantidade_estoque >= 0");
                    table.CheckConstraint("ck_produtos_quantidade_minima_nao_negativa", "quantidade_minima >= 0");
                });

            migrationBuilder.CreateTable(
                name: "movimentos_estoque",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    produto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantidade = table.Column<int>(type: "integer", nullable: false),
                    valor_unitario = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    quantidade_antes = table.Column<int>(type: "integer", nullable: false),
                    quantidade_depois = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fornecedor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimentos_estoque", x => x.id);
                    table.ForeignKey(
                        name: "fk_movimentos_estoque_produtos_produto_id",
                        column: x => x.produto_id,
                        principalTable: "produtos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_movimentos_estoque_produto_id_data",
                table: "movimentos_estoque",
                columns: new[] { "produto_id", "data" });

            migrationBuilder.CreateIndex(
                name: "ix_produtos_negocio_id_nome",
                table: "produtos",
                columns: new[] { "negocio_id", "nome" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movimentos_estoque");

            migrationBuilder.DropTable(
                name: "produtos");
        }
    }
}
