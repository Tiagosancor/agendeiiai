using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class RedefinicaoSenhaConfiguracao : IEntityTypeConfiguration<RedefinicaoSenha>
{
    public void Configure(EntityTypeBuilder<RedefinicaoSenha> builder)
    {
        builder.ToTable("redefinicoes_senha");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Hash).HasMaxLength(100).IsRequired();
        builder.Property(r => r.ExpiraEm).IsRequired();

        // xmin: dois envios simultâneos do mesmo link — só o primeiro grava (uso único).
        builder.Property<uint>("Versao").IsRowVersion();

        builder.HasIndex(r => r.Hash).IsUnique();
        builder.HasIndex(r => new { r.UsuarioId, r.CriadoEm });

        builder.HasOne<Usuario>().WithMany().HasForeignKey(r => r.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
