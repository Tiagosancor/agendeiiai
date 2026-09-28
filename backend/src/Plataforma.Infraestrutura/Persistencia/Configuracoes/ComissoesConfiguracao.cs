using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Comissoes;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class PeriodoComissaoConfiguracao : IEntityTypeConfiguration<PeriodoComissao>
{
    public void Configure(EntityTypeBuilder<PeriodoComissao> builder)
    {
        builder.ToTable("periodos_comissao", t =>
            t.HasCheckConstraint("ck_periodos_comissao_inicio_antes_do_fim", "inicio <= fim"));

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Inicio).IsRequired();
        builder.Property(p => p.Fim).IsRequired();
        builder.Property(p => p.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();

        // xmin: fechar/reabrir a mesma quinzena ao mesmo tempo — só o primeiro grava.
        builder.Property<uint>("Versao").IsRowVersion();

        builder.HasIndex(p => new { p.NegocioId, p.Inicio });

        // A não sobreposição (exclusion constraint em daterange) é criada em SQL puro na migration.
    }
}

public sealed class FechamentoComissaoConfiguracao : IEntityTypeConfiguration<FechamentoComissao>
{
    public void Configure(EntityTypeBuilder<FechamentoComissao> builder)
    {
        builder.ToTable("fechamentos_comissao");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.TotalCobrado).HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(f => f.TotalComissao).HasColumnType("numeric(12,2)").IsRequired();

        builder.HasOne<PeriodoComissao>().WithMany().HasForeignKey(f => f.PeriodoComissaoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(f => new { f.PeriodoComissaoId, f.ProfissionalId }).IsUnique();
        builder.HasIndex(f => f.ProfissionalId);
    }
}
