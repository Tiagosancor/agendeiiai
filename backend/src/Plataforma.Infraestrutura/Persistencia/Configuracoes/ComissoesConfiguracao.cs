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
        builder.ToTable("fechamentos_comissao", t => t.HasCheckConstraint(
            "ck_fechamentos_comissao_uma_pessoa", "(profissional_id IS NULL) <> (usuario_id IS NULL)"));
        builder.Ignore(f => f.Pessoa);

        builder.HasKey(f => f.Id);

        builder.Property(f => f.TotalCobrado).HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(f => f.TotalComissao).HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(f => f.TotalComissaoProdutos).HasColumnType("numeric(12,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(f => f.TotalVales).HasColumnType("numeric(12,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(f => f.TotalConsumo).HasColumnType("numeric(12,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(f => f.Liquido).HasColumnType("numeric(12,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(f => f.SaldoRestante).HasColumnType("numeric(12,2)").HasDefaultValue(0m).IsRequired();

        builder.HasOne<PeriodoComissao>().WithMany().HasForeignKey(f => f.PeriodoComissaoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(f => new { f.PeriodoComissaoId, f.ProfissionalId }).IsUnique();
        builder.HasIndex(f => f.ProfissionalId);
        builder.HasIndex(f => new { f.PeriodoComissaoId, f.UsuarioId }).IsUnique();
        builder.HasIndex(f => f.UsuarioId);
    }
}

public sealed class LancamentoSaldoDevedorConfiguracao : IEntityTypeConfiguration<LancamentoSaldoDevedor>
{
    public void Configure(EntityTypeBuilder<LancamentoSaldoDevedor> builder)
    {
        builder.ToTable("lancamentos_saldo_devedor", t =>
        {
            t.HasCheckConstraint("ck_lancamentos_saldo_devedor_valor", "valor >= 0");
            t.HasCheckConstraint("ck_lancamentos_saldo_devedor_uma_pessoa", "(profissional_id IS NULL) <> (usuario_id IS NULL)");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.Valor).HasColumnType("numeric(12,2)");
        builder.Property(l => l.ValorUnitario).HasColumnType("numeric(10,2)");
        builder.Property(l => l.Descricao).HasMaxLength(LancamentoSaldoDevedor.TamanhoMaximoDescricao);
        builder.Property(l => l.NomeProduto).HasMaxLength(200);
        builder.HasIndex(l => new { l.ProfissionalId, l.Data });
        builder.HasIndex(l => new { l.UsuarioId, l.Data });
    }
}

public sealed class QuitacaoSaldoConfiguracao : IEntityTypeConfiguration<QuitacaoSaldo>
{
    public void Configure(EntityTypeBuilder<QuitacaoSaldo> builder)
    {
        builder.ToTable("quitacoes_saldo", t => t.HasCheckConstraint("ck_quitacoes_saldo_valor", "valor > 0"));
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Valor).HasColumnType("numeric(12,2)");
        // Reabrir a quinzena apaga os fechamentos e, com eles, as quitações: o saldo volta a ficar em aberto.
        builder.HasOne<FechamentoComissao>().WithMany().HasForeignKey(q => q.FechamentoComissaoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LancamentoSaldoDevedor>().WithMany().HasForeignKey(q => q.LancamentoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(q => q.LancamentoId);
    }
}
