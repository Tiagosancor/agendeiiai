using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Servicos;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class ProfissionalServicoConfiguracao : IEntityTypeConfiguration<ProfissionalServico>
{
    public void Configure(EntityTypeBuilder<ProfissionalServico> builder)
    {
        builder.ToTable("profissional_servicos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PrecoPersonalizado).HasColumnType("numeric(10,2)");
        builder.Property(p => p.DuracaoPersonalizadaMinutos);

        builder.HasIndex(p => new { p.ProfissionalId, p.ServicoId }).IsUnique();
    }
}
