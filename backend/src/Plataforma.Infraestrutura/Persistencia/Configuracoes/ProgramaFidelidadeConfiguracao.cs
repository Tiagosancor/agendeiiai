using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Fidelidade;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class ProgramaFidelidadeConfiguracao : IEntityTypeConfiguration<ProgramaFidelidade>
{
    public void Configure(EntityTypeBuilder<ProgramaFidelidade> builder)
    {
        builder.ToTable("programas_fidelidade");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.DescricaoRecompensa).HasMaxLength(500).IsRequired();

        // No máximo um programa por negócio (seção 7).
        builder.HasIndex(p => p.NegocioId).IsUnique();
    }
}
