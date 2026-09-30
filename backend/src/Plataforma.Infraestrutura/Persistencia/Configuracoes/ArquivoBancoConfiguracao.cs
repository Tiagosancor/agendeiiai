using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Arquivos;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class ArquivoBancoConfiguracao : IEntityTypeConfiguration<ArquivoBanco>
{
    public void Configure(EntityTypeBuilder<ArquivoBanco> builder)
    {
        builder.ToTable("arquivos");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Chave).HasMaxLength(64).IsRequired();
        builder.HasIndex(a => a.Chave).IsUnique();
        builder.Property(a => a.TipoConteudo).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Conteudo).IsRequired();

        builder.HasOne<Negocio>().WithMany().HasForeignKey(a => a.NegocioId).OnDelete(DeleteBehavior.Cascade);
    }
}
