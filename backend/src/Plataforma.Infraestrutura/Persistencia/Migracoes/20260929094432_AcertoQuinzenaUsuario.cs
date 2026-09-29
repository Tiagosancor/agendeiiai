using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AcertoQuinzenaUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "acerto_por_quinzena",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "profissional_id",
                table: "lancamentos_saldo_devedor",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "usuario_id",
                table: "lancamentos_saldo_devedor",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "profissional_id",
                table: "fechamentos_comissao",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "usuario_id",
                table: "fechamentos_comissao",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_saldo_devedor_usuario_id_data",
                table: "lancamentos_saldo_devedor",
                columns: new[] { "usuario_id", "data" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_lancamentos_saldo_devedor_uma_pessoa",
                table: "lancamentos_saldo_devedor",
                sql: "(profissional_id IS NULL) <> (usuario_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_fechamentos_comissao_periodo_comissao_id_usuario_id",
                table: "fechamentos_comissao",
                columns: new[] { "periodo_comissao_id", "usuario_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fechamentos_comissao_usuario_id",
                table: "fechamentos_comissao",
                column: "usuario_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fechamentos_comissao_uma_pessoa",
                table: "fechamentos_comissao",
                sql: "(profissional_id IS NULL) <> (usuario_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_lancamentos_saldo_devedor_usuario_id_data",
                table: "lancamentos_saldo_devedor");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lancamentos_saldo_devedor_uma_pessoa",
                table: "lancamentos_saldo_devedor");

            migrationBuilder.DropIndex(
                name: "ix_fechamentos_comissao_periodo_comissao_id_usuario_id",
                table: "fechamentos_comissao");

            migrationBuilder.DropIndex(
                name: "ix_fechamentos_comissao_usuario_id",
                table: "fechamentos_comissao");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fechamentos_comissao_uma_pessoa",
                table: "fechamentos_comissao");

            migrationBuilder.DropColumn(
                name: "acerto_por_quinzena",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "usuario_id",
                table: "lancamentos_saldo_devedor");

            migrationBuilder.DropColumn(
                name: "usuario_id",
                table: "fechamentos_comissao");

            migrationBuilder.AlterColumn<Guid>(
                name: "profissional_id",
                table: "lancamentos_saldo_devedor",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "profissional_id",
                table: "fechamentos_comissao",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
