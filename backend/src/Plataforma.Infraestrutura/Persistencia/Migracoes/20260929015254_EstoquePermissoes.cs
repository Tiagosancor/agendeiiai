using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <summary>
    /// Seção 7, "Estoque": "vender produtos" ligada para Administrador e Recepcionista; "gerenciar estoque" só
    /// para o Administrador. Separada da migration das tabelas: a versão anterior da API não conhece os valores
    /// do enum e quebraria o login ao ler as permissões — em produção, rodar só depois do deploy.
    /// </summary>
    public partial class EstoquePermissoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO usuario_permissoes (id, negocio_id, usuario_id, permissao, criado_em)
                SELECT gen_random_uuid(), u.negocio_id, u.id, 'VenderProdutos', now()
                FROM usuarios u
                WHERE u.perfil IN ('Administrador', 'Recepcionista') AND u.excluido = false
                ON CONFLICT (usuario_id, permissao) DO NOTHING;

                INSERT INTO usuario_permissoes (id, negocio_id, usuario_id, permissao, criado_em)
                SELECT gen_random_uuid(), u.negocio_id, u.id, 'GerenciarEstoque', now()
                FROM usuarios u
                WHERE u.perfil = 'Administrador' AND u.excluido = false
                ON CONFLICT (usuario_id, permissao) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM usuario_permissoes WHERE permissao IN ('VenderProdutos', 'GerenciarEstoque');");
        }
    }
}
