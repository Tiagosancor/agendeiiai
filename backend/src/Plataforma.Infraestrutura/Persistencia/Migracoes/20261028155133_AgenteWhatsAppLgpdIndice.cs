using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AgenteWhatsAppLgpdIndice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_mensagens_whatsapp_negocio_id_criado_em",
                table: "mensagens_whatsapp",
                columns: new[] { "negocio_id", "criado_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mensagens_whatsapp_negocio_id_criado_em",
                table: "mensagens_whatsapp");
        }
    }
}
