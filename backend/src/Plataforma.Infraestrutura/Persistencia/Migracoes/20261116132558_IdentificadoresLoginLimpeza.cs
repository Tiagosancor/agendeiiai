using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infraestrutura.Persistencia.Migracoes
{
    /// <summary>
    /// DEPOIS do deploy (parte 2 da <c>IdentificadoresLoginUsuario</c>): os usuários excluídos antes do login por identificadores
    /// guardam <c>""</c> no e-mail; a versão nova grava NULL. Só depois de a versão antiga sair do ar é seguro trocar — ela
    /// materializa <c>Email</c> como não anulável e quebraria ao ler um excluído com NULL. Sem mudança de modelo.
    /// </summary>
    public partial class IdentificadoresLoginLimpeza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE usuarios SET email = NULL WHERE email = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Volta ao formato que a versão anterior espera: e-mail vazio (e não nulo) nos excluídos. Quem nunca teve e-mail e
            // continua ativo permanece NULL (o rollback do plano exige o UPDATE manual documentado).
            migrationBuilder.Sql("UPDATE usuarios SET email = '' WHERE email IS NULL AND excluido = true;");
        }
    }
}
