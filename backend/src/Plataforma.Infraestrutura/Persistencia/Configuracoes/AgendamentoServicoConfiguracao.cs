using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Agendamentos;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class AgendamentoServicoConfiguracao : IEntityTypeConfiguration<AgendamentoServico>
{
    public void Configure(EntityTypeBuilder<AgendamentoServico> builder)
    {
        builder.ToTable("agendamento_servicos");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Nome).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Preco).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(s => s.DuracaoMinutos).IsRequired();
    }
}
