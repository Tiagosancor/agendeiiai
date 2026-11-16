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

        // Convite (administrador) x redefinição; o convite usado verifica o e-mail se ele ainda for o do hash.
        builder.Property(r => r.Tipo).HasConversion<string>().HasMaxLength(20).HasDefaultValue(TipoRedefinicaoSenha.Redefinicao).IsRequired();
        builder.Property(r => r.EmailDestinoHash).HasMaxLength(64);

        // xmin: dois envios simultâneos do mesmo link — só o primeiro grava (uso único).
        builder.Property<uint>("Versao").IsRowVersion();

        builder.HasIndex(r => r.Hash).IsUnique();
        builder.HasIndex(r => new { r.UsuarioId, r.CriadoEm });

        builder.HasOne<Usuario>().WithMany().HasForeignKey(r => r.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
