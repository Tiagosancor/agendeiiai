using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class TrocaLinkNegocio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "slug_alterado_em",
                table: "negocios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "slugs_anteriores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    trocado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    redireciona_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slugs_anteriores", x => x.id);
                    table.ForeignKey(
                        name: "fk_slugs_anteriores_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_slugs_anteriores_negocio_id",
                table: "slugs_anteriores",
                column: "negocio_id");

            migrationBuilder.CreateIndex(
                name: "ix_slugs_anteriores_slug_redireciona_ate",
                table: "slugs_anteriores",
                columns: new[] { "slug", "redireciona_ate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "slugs_anteriores");

            migrationBuilder.DropColumn(
                name: "slug_alterado_em",
                table: "negocios");
        }
    }
}
