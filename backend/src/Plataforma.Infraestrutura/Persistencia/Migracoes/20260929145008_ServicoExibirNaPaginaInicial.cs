using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ServicoExibirNaPaginaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "exibir_na_pagina_inicial",
                table: "servicos",
                type: "boolean",
                nullable: false,
                // Serviços que já existem continuam na vitrine (e a versão no ar, que não conhece a coluna, cria com true).
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "exibir_na_pagina_inicial",
                table: "servicos");
        }
    }
}
