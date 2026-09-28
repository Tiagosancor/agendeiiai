using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class EncaixeAtendimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "telefone",
                table: "clientes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mensagens_autorizadas_em",
                table: "agendamentos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "mensagens_autorizadas_por_usuario_id",
                table: "agendamentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origem",
                table: "agendamentos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Painel");

            // Origem dos agendamentos que já existiam: quem passou pelo consentimento do assistente
            // público veio do link; o resto foi lançado no painel.
            migrationBuilder.Sql("UPDATE agendamentos SET origem = 'LinkPublico' WHERE consentimento_data IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mensagens_autorizadas_em",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "mensagens_autorizadas_por_usuario_id",
                table: "agendamentos");

            migrationBuilder.DropColumn(
                name: "origem",
                table: "agendamentos");

            migrationBuilder.AlterColumn<string>(
                name: "telefone",
                table: "clientes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
