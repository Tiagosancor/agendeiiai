using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Agendamentos;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class AjusteValorAtendimentoConfiguracao : IEntityTypeConfiguration<AjusteValorAtendimento>
{
    public void Configure(EntityTypeBuilder<AjusteValorAtendimento> builder)
    {
        builder.ToTable("ajustes_valor_atendimento");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Modo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.ValorInformado).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(a => a.PrecoOriginal).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(a => a.ValorAntes).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(a => a.ValorDepois).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(a => a.Motivo).HasMaxLength(AjusteValorAtendimento.TamanhoMaximoMotivo).IsRequired();

        builder.HasOne<Agendamento>().WithMany().HasForeignKey(a => a.AgendamentoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => a.AgendamentoId);
    }
}
