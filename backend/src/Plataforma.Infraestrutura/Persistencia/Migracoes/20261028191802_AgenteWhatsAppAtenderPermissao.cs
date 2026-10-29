using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <summary>
    /// Concede "atender as conversas do WhatsApp" (<c>AtenderConversasWhatsApp</c>, valor 21) aos Administradores. Roda só
    /// <b>depois</b> do deploy: a versão anterior da API converte cada permissão gravada no enum e quebraria o login com um valor que
    /// não conhece. Rollback de código para antes desta fatia com esta migration aplicada exige apagar a permissão antes (o Down faz).
    /// </summary>
    public partial class AgenteWhatsAppAtenderPermissao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO usuario_permissoes (id, negocio_id, usuario_id, permissao, criado_em)
                SELECT gen_random_uuid(), u.negocio_id, u.id, 'AtenderConversasWhatsApp', now()
                FROM usuarios u
                WHERE u.perfil = 'Administrador' AND u.excluido = false
                ON CONFLICT (usuario_id, permissao) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM usuario_permissoes WHERE permissao = 'AtenderConversasWhatsApp';");
        }
    }
}
