using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class UsuarioPermissaoConfiguracao : IEntityTypeConfiguration<UsuarioPermissao>
{
    public void Configure(EntityTypeBuilder<UsuarioPermissao> builder)
    {
        builder.ToTable("usuario_permissoes");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Permissao).HasConversion<string>().HasMaxLength(50).IsRequired();

        builder.HasIndex(p => new { p.UsuarioId, p.Permissao }).IsUnique();
    }
}
