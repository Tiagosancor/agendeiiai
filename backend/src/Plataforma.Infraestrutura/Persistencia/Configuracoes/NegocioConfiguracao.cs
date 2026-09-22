using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Negocios;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class NegocioConfiguracao : IEntityTypeConfiguration<Negocio>
{
    public void Configure(EntityTypeBuilder<Negocio> builder)
    {
        builder.ToTable("negocios");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Slug)
            .HasConversion(slug => slug.Valor, valor => Slug.Criar(valor))
            .HasMaxLength(Slug.TamanhoMaximo)
            .IsRequired();

        builder.HasIndex(n => n.Slug)
            .IsUnique();

        builder.Property(n => n.NomeExibido)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(n => n.Tipo)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(n => n.Fuso)
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(n => n.Ativo)
            .IsRequired();

        builder.Property(n => n.CriadoEm)
            .IsRequired();
    }
}
