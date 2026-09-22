using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Agendamentos;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class AgendamentoConfiguracao : IEntityTypeConfiguration<Agendamento>
{
    public void Configure(EntityTypeBuilder<Agendamento> builder)
    {
        builder.ToTable("agendamentos");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Inicio).IsRequired();
        builder.Property(a => a.Fim).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Observacoes).HasMaxLength(2000);
        builder.Property(a => a.NomeInformado).HasMaxLength(200);
        builder.Property(a => a.ReservadoAte);
        builder.Property(a => a.DescontoAplicado).HasColumnType("numeric(10,2)").HasDefaultValue(0m);

        // Calculado a partir dos serviços e do desconto — nunca uma coluna própria (seção 6.2.4).
        builder.Ignore(a => a.Total);

        // Índice de apoio às consultas de agenda/disponibilidade (por profissional e dia) —
        // a garantia de não-sobreposição de verdade é a exclusion constraint (migration
        // manual em SQL puro, seção 8.2.1), que este índice não substitui.
        builder.HasIndex(a => new { a.ProfissionalId, a.Inicio });

        builder.HasMany(a => a.Servicos)
            .WithOne()
            .HasForeignKey(s => s.AgendamentoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Servicos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
