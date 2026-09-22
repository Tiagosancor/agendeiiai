using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class UsuariosCadastros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cor_primaria",
                table: "negocios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cor_secundaria",
                table: "negocios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_bairro",
                table: "negocios",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_cep",
                table: "negocios",
                type: "character varying(9)",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_cidade",
                table: "negocios",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_numero",
                table: "negocios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endereco_rua",
                table: "negocios",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                table: "negocios",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rede_social_facebook",
                table: "negocios",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rede_social_instagram",
                table: "negocios",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rede_social_whatsapp",
                table: "negocios",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subtitulo_pagina",
                table: "negocios",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "telefone",
                table: "negocios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "texto_sobre",
                table: "negocios",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "titulo_pagina",
                table: "negocios",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "categorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ativa = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categorias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    observacoes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clientes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "negocio_horario_funcionamento",
                columns: table => new
                {
                    dia_semana = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    abertura = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    fechamento = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    fechado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_negocio_horario_funcionamento", x => new { x.negocio_id, x.dia_semana });
                    table.ForeignKey(
                        name: "fk_negocio_horario_funcionamento_negocios_negocio_id",
                        column: x => x.negocio_id,
                        principalTable: "negocios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profissionais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    endereco_bairro = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    endereco_cidade = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    endereco_rua = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    endereco_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    endereco_cep = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    cpf_mascarado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cpf_texto_cifrado = table.Column<byte[]>(type: "bytea", nullable: true),
                    cpf_nonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    cpf_chave_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    foto_url = table.Column<string>(type: "text", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profissionais", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tokens_atualizacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revogado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tokens_atualizacao", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    endereco_bairro = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    endereco_cidade = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    endereco_rua = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    endereco_numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    endereco_cep = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    cpf_mascarado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cpf_texto_cifrado = table.Column<byte[]>(type: "bytea", nullable: true),
                    cpf_nonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    cpf_chave_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    foto_url = table.Column<string>(type: "text", nullable: true),
                    senha_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    perfil = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    profissional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "servicos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    categoria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    preco = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    duracao_minutos = table.Column<int>(type: "integer", nullable: false),
                    popular = table.Column<bool>(type: "boolean", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_servicos", x => x.id);
                    table.ForeignKey(
                        name: "fk_servicos_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalTable: "categorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usuario_permissoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permissao = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_permissoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_permissoes_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_clientes_negocio_id_telefone",
                table: "clientes",
                columns: new[] { "negocio_id", "telefone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_servicos_categoria_id",
                table: "servicos",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_tokens_atualizacao_hash",
                table: "tokens_atualizacao",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_permissoes_usuario_id_permissao",
                table: "usuario_permissoes",
                columns: new[] { "usuario_id", "permissao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.DropTable(
                name: "negocio_horario_funcionamento");

            migrationBuilder.DropTable(
                name: "profissionais");

            migrationBuilder.DropTable(
                name: "servicos");

            migrationBuilder.DropTable(
                name: "tokens_atualizacao");

            migrationBuilder.DropTable(
                name: "usuario_permissoes");

            migrationBuilder.DropTable(
                name: "categorias");

            migrationBuilder.DropTable(
                name: "usuarios");

            migrationBuilder.DropColumn(
                name: "cor_primaria",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "cor_secundaria",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "endereco_bairro",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "endereco_cep",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "endereco_cidade",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "endereco_numero",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "endereco_rua",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "logo_url",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "rede_social_facebook",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "rede_social_instagram",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "rede_social_whatsapp",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "subtitulo_pagina",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "telefone",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "texto_sobre",
                table: "negocios");

            migrationBuilder.DropColumn(
                name: "titulo_pagina",
                table: "negocios");
        }
    }
}
