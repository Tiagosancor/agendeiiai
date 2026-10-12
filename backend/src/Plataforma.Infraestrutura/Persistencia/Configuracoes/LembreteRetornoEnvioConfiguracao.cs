using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class LembreteRetornoEnvioConfiguracao : IEntityTypeConfiguration<LembreteRetornoEnvio>
{
    public void Configure(EntityTypeBuilder<LembreteRetornoEnvio> builder)
    {
        builder.ToTable("lembretes_retorno_envios");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Erro).HasMaxLength(500);
        builder.Property(e => e.IdMensagem).HasMaxLength(200);

        // Idempotência: um lembrete por atendimento de referência, mesmo com duas execuções do job ao mesmo tempo.
        builder.HasIndex(e => new { e.ClienteId, e.AgendamentoId }).IsUnique();

        // Relatório por período.
        builder.HasIndex(e => new { e.NegocioId, e.EnviadoEm });

        // xmin: a nova tentativa de uma falha não sai duas vezes se duas execuções a pegarem juntas.
        builder.Property<uint>("Versao").IsRowVersion();

        builder.HasOne<Cliente>().WithMany().HasForeignKey(e => e.ClienteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Agendamento>().WithMany().HasForeignKey(e => e.AgendamentoId).OnDelete(DeleteBehavior.Cascade);
    }
}
