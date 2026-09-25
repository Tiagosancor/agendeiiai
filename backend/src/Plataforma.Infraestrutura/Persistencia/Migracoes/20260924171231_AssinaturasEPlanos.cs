using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class AssinaturasEPlanos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eventos_webhook_pagamento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    id_evento = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_webhook_pagamento", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "logs_auditoria_plataforma",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    acao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: true),
                    detalhes = table.Column<string>(type: "text", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_logs_auditoria_plataforma", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    minimo_profissionais = table.Column<int>(type: "integer", nullable: false),
                    maximo_profissionais = table.Column<int>(type: "integer", nullable: false),
                    preco_mensal = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    preco_anual_por_mes = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    destaque = table.Column<bool>(type: "boolean", nullable: false),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assinaturas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plano_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodicidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    preco_mensal_travado = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fim_teste = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proximo_vencimento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    carencia_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_aviso_dias = table.Column<int>(type: "integer", nullable: true),
                    provedor_gateway = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    id_externo_gateway = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assinaturas", x => x.id);
                    table.ForeignKey(
                        name: "fk_assinaturas_planos_plano_id",
                        column: x => x.plano_id,
                        principalTable: "planos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cobrancas_assinatura",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assinatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    forma = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pago_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    periodo_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    periodo_fim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_externo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    autor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobrancas_assinatura", x => x.id);
                    table.ForeignKey(
                        name: "fk_cobrancas_assinatura_assinaturas_assinatura_id",
                        column: x => x.assinatura_id,
                        principalTable: "assinaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "historico_assinaturas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negocio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assinatura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado_anterior = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    estado_novo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    autor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historico_assinaturas", x => x.id);
                    table.ForeignKey(
                        name: "fk_historico_assinaturas_assinaturas_assinatura_id",
                        column: x => x.assinatura_id,
                        principalTable: "assinaturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "planos",
                columns: new[] { "id", "ativo", "atualizado_em", "criado_em", "destaque", "maximo_profissionais", "minimo_profissionais", "nome", "ordem", "preco_anual_por_mes", "preco_mensal" },
                values: new object[,]
                {
                    { new Guid("6f1c2a3b-4d5e-4f60-8a71-000000000001"), true, null, new DateTimeOffset(new DateTime(2026, 9, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, 3, 1, "Começo", 1, 38.90m, 49.90m },
                    { new Guid("6f1c2a3b-4d5e-4f60-8a71-000000000002"), true, null, new DateTimeOffset(new DateTime(2026, 9, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, 7, 4, "Ritmo", 2, 68.90m, 79.90m },
                    { new Guid("6f1c2a3b-4d5e-4f60-8a71-000000000003"), true, null, new DateTimeOffset(new DateTime(2026, 9, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, 12, 8, "Casa Cheia", 3, 88.90m, 99.90m }
                });

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_estado",
                table: "assinaturas",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_negocio_id",
                table: "assinaturas",
                column: "negocio_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assinaturas_plano_id",
                table: "assinaturas",
                column: "plano_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_assinatura_assinatura_id_pago_em",
                table: "cobrancas_assinatura",
                columns: new[] { "assinatura_id", "pago_em" });

            migrationBuilder.CreateIndex(
                name: "ix_cobrancas_assinatura_origem_id_externo",
                table: "cobrancas_assinatura",
                columns: new[] { "origem", "id_externo" },
                unique: true,
                filter: "id_externo IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_eventos_webhook_pagamento_provedor_id_evento",
                table: "eventos_webhook_pagamento",
                columns: new[] { "provedor", "id_evento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_historico_assinaturas_assinatura_id",
                table: "historico_assinaturas",
                column: "assinatura_id");

            migrationBuilder.CreateIndex(
                name: "ix_logs_auditoria_plataforma_criado_em",
                table: "logs_auditoria_plataforma",
                column: "criado_em");

            migrationBuilder.CreateIndex(
                name: "ix_logs_auditoria_plataforma_negocio_id",
                table: "logs_auditoria_plataforma",
                column: "negocio_id");

            // Negócios anteriores às assinaturas ganham uma assinatura Ativa SEM vencimento, para
            // não serem bloqueados. Plano escolhido pelos profissionais ativos de hoje, para o
            // limite não travar ninguém (acima de 12, fica no maior).
            migrationBuilder.Sql("""
                INSERT INTO assinaturas (id, negocio_id, plano_id, periodicidade, preco_mensal_travado, estado, criado_em)
                SELECT gen_random_uuid(), n.id, p.id, 'Mensal', p.preco_mensal, 'Ativa', now()
                FROM negocios n
                CROSS JOIN LATERAL (
                    SELECT count(*) AS ativos FROM profissionais pr WHERE pr.negocio_id = n.id AND pr.ativo
                ) c
                JOIN planos p ON p.id = CASE
                    WHEN c.ativos <= 3 THEN '6f1c2a3b-4d5e-4f60-8a71-000000000001'::uuid
                    WHEN c.ativos <= 7 THEN '6f1c2a3b-4d5e-4f60-8a71-000000000002'::uuid
                    ELSE '6f1c2a3b-4d5e-4f60-8a71-000000000003'::uuid
                END;

                INSERT INTO historico_assinaturas (id, negocio_id, assinatura_id, estado_anterior, estado_novo, autor, motivo, criado_em)
                SELECT gen_random_uuid(), a.negocio_id, a.id, NULL, 'Ativa', 'migracao',
                       'Negócio anterior às assinaturas: ativa sem vencimento', now()
                FROM assinaturas a;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cobrancas_assinatura");

            migrationBuilder.DropTable(
                name: "eventos_webhook_pagamento");

            migrationBuilder.DropTable(
                name: "historico_assinaturas");

            migrationBuilder.DropTable(
                name: "logs_auditoria_plataforma");

            migrationBuilder.DropTable(
                name: "assinaturas");

            migrationBuilder.DropTable(
                name: "planos");
        }
    }
}
