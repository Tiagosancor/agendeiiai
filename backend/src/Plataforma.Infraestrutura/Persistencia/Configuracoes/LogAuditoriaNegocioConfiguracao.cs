using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Auditoria;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class LogAuditoriaNegocioConfiguracao : IEntityTypeConfiguration<LogAuditoriaNegocio>
{
    public void Configure(EntityTypeBuilder<LogAuditoriaNegocio> builder)
    {
        builder.ToTable("logs_auditoria_negocio");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Autor).HasMaxLength(320).IsRequired();
        builder.Property(l => l.Acao).HasMaxLength(100).IsRequired();
        builder.Property(l => l.Entidade).HasMaxLength(50).IsRequired();
        builder.Property(l => l.Detalhes).HasMaxLength(1000);

        builder.HasIndex(l => new { l.NegocioId, l.CriadoEm });
        builder.HasIndex(l => l.AutorUsuarioId);
        builder.HasIndex(l => new { l.Entidade, l.EntidadeId });
    }
}
