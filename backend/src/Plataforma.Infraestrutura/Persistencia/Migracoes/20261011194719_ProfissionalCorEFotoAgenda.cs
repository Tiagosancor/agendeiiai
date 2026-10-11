using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ProfissionalCorEFotoAgenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cor_agenda",
                table: "profissionais",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "foto_arquivo",
                table: "profissionais",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_profissionais_cor_agenda",
                table: "profissionais",
                sql: "cor_agenda IS NULL OR cor_agenda ~ '^#[0-9a-f]{6}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_profissionais_cor_agenda",
                table: "profissionais");

            migrationBuilder.DropColumn(
                name: "cor_agenda",
                table: "profissionais");

            migrationBuilder.DropColumn(
                name: "foto_arquivo",
                table: "profissionais");
        }
    }
}
