using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Servicos;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class CategoriaConfiguracao : IEntityTypeConfiguration<Categoria>
{
    public void Configure(EntityTypeBuilder<Categoria> builder)
    {
        builder.ToTable("categorias");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Nome).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Ativa).IsRequired();
    }
}
