using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <summary>
    /// Seção 7: "gerenciar comissões" e "ver comissões de todos" são do Administrador por padrão.
    /// Quem já é Administrador ganha as duas agora (as contas novas ganham pelo PermissoesPadrao).
    /// Separada da migration das colunas de propósito: a versão anterior da API não conhece esses
    /// valores do enum e quebraria ao ler as permissões — em produção, rodar só depois do deploy.
    /// </summary>
    public partial class ComissoesPermissoesAdministrador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO usuario_permissoes (id, negocio_id, usuario_id, permissao, criado_em)
                SELECT gen_random_uuid(), u.negocio_id, u.id, p.permissao, now()
                FROM usuarios u
                CROSS JOIN (VALUES ('GerenciarComissoes'), ('VerComissoesDeTodos')) AS p(permissao)
                WHERE u.perfil = 'Administrador' AND u.excluido = false
                ON CONFLICT (usuario_id, permissao) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM usuario_permissoes WHERE permissao IN ('GerenciarComissoes', 'VerComissoesDeTodos');");
        }
    }
}
