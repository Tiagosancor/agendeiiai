using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Estoque;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class ProdutoConfiguracao : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.ToTable("produtos", tabela =>
        {
            // Garantia final contra estoque negativo, além da trava da linha (seção 7: nunca fica negativo).
            tabela.HasCheckConstraint("ck_produtos_quantidade_estoque_nao_negativa", "quantidade_estoque >= 0");
            tabela.HasCheckConstraint("ck_produtos_quantidade_minima_nao_negativa", "quantidade_minima >= 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Nome).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Categoria).HasMaxLength(100);
        builder.Property(p => p.PrecoCusto).HasColumnType("numeric(10,2)");
        builder.Property(p => p.PrecoVenda).HasColumnType("numeric(10,2)");
        builder.Ignore(p => p.EstoqueBaixo);
        builder.Ignore(p => p.Esgotado);
        builder.HasIndex(p => new { p.NegocioId, p.Nome });
    }
}

public sealed class MovimentoEstoqueConfiguracao : IEntityTypeConfiguration<MovimentoEstoque>
{
    public void Configure(EntityTypeBuilder<MovimentoEstoque> builder)
    {
        builder.ToTable("movimentos_estoque");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.ValorUnitario).HasColumnType("numeric(10,2)");
        builder.Property(m => m.Observacao).HasMaxLength(500);
        builder.Property(m => m.Fornecedor).HasMaxLength(200);
        builder.HasOne<Produto>().WithMany().HasForeignKey(m => m.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => new { m.ProdutoId, m.Data });
    }
}
