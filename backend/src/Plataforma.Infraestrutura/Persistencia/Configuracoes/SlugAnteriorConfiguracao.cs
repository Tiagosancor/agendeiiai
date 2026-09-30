using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Negocios;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class SlugAnteriorConfiguracao : IEntityTypeConfiguration<SlugAnterior>
{
    public void Configure(EntityTypeBuilder<SlugAnterior> builder)
    {
        builder.ToTable("slugs_anteriores");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Slug)
            .HasConversion(slug => slug.Valor, valor => Slug.Criar(valor))
            .HasMaxLength(Slug.TamanhoMaximo)
            .IsRequired();

        // Não é único: depois dos 90 dias o mesmo link pode ser de outro negócio e virar "antigo" de novo.
        builder.HasIndex(s => new { s.Slug, s.RedirecionaAte });

        builder.HasOne<Negocio>().WithMany().HasForeignKey(s => s.NegocioId).OnDelete(DeleteBehavior.Cascade);
    }
}
