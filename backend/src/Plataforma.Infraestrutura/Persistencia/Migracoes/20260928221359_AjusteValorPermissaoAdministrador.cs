using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <summary>
    /// Seção 7: "ajustar valor do atendimento" é desligada para todos os perfis e só o Administrador a
    /// tem de origem — os Administradores que já existem ganham agora; ninguém mais. Separada da
    /// migration das colunas: a versão anterior da API não conhece o valor do enum e quebraria o login
    /// ao ler as permissões — em produção, rodar só depois do deploy.
    /// </summary>
    public partial class AjusteValorPermissaoAdministrador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO usuario_permissoes (id, negocio_id, usuario_id, permissao, criado_em)
                SELECT gen_random_uuid(), u.negocio_id, u.id, 'AjustarValorAtendimento', now()
                FROM usuarios u
                WHERE u.perfil = 'Administrador' AND u.excluido = false
                ON CONFLICT (usuario_id, permissao) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM usuario_permissoes WHERE permissao = 'AjustarValorAtendimento';");
        }
    }
}
