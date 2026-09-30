using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class FundoPaginaNegocio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cor_fundo",
                table: "negocios",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "imagem_fundo",
                table: "negocios",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "arquivos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo_conteudo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    conteudo = table.Column<byte[]>(type: "bytea", nullable: false),
                    tamanho = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_arquivos", x => x.id);
                    table.ForeignKey(
                        name: "fk_arquivos_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_arquivos_chave",
                table: "arquivos",
                column: "chave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_arquivos_negocio_id",
                table: "arquivos",
                column: "negocio_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "arquivos");

            migrationBuilder.DropColumn(
                name: "cor_fundo",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "imagem_fundo",
                table: "negocios");
        }
    }
}
