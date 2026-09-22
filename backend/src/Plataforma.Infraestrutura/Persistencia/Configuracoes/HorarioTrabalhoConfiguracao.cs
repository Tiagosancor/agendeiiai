using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Profissionais;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class HorarioTrabalhoConfiguracao : IEntityTypeConfiguration<HorarioTrabalho>
{
    public void Configure(EntityTypeBuilder<HorarioTrabalho> builder)
    {
        builder.ToTable("horarios_trabalho");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.DiaSemana).HasConversion<string>().HasMaxLength(15).IsRequired();
        builder.Property(h => h.Inicio).IsRequired();
        builder.Property(h => h.Fim).IsRequired();

        builder.HasIndex(h => new { h.ProfissionalId, h.DiaSemana });
    }
}
