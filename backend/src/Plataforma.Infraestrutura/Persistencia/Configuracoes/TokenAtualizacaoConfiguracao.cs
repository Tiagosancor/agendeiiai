using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class TokenAtualizacaoConfiguracao : IEntityTypeConfiguration<TokenAtualizacao>
{
    public void Configure(EntityTypeBuilder<TokenAtualizacao> builder)
    {
        builder.ToTable("tokens_atualizacao");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Hash).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ExpiraEm).IsRequired();

        builder.HasIndex(t => t.Hash).IsUnique();
    }
}
