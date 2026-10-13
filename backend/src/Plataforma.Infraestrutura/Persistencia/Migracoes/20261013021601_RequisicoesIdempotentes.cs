using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class RequisicoesIdempotentes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requisicoes_idempotentes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    escopo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    chave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    hash_requisicao = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status_http = table.Column<int>(type: "integer", nullable: false),
                    resultado = table.Column<string>(type: "jsonb", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisicoes_idempotentes", x => x.id);
                    table.CheckConstraint("ck_requisicoes_idempotentes_resultado_tamanho", "octet_length(resultado::text) <= 8192");
                    table.ForeignKey(
                        name: "fk_requisicoes_idempotentes_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_requisicoes_idempotentes_expira_em",
                table: "requisicoes_idempotentes",
                column: "expira_em");

            migrationBuilder.CreateIndex(
                name: "ix_requisicoes_idempotentes_negocio_id_principal_escopo_chave",
                table: "requisicoes_idempotentes",
                columns: new[] { "negocio_id", "principal", "escopo", "chave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requisicoes_idempotentes");
        }
    }
}
