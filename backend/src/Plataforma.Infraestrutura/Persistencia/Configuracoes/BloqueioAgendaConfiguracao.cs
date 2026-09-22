using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Profissionais;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class BloqueioAgendaConfiguracao : IEntityTypeConfiguration<BloqueioAgenda>
{
    public void Configure(EntityTypeBuilder<BloqueioAgenda> builder)
    {
        builder.ToTable("bloqueios_agenda");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.InicioUtc).IsRequired();
        builder.Property(b => b.FimUtc).IsRequired();
        builder.Property(b => b.Motivo).HasMaxLength(500);

        builder.HasIndex(b => new { b.ProfissionalId, b.InicioUtc, b.FimUtc });
    }
}
