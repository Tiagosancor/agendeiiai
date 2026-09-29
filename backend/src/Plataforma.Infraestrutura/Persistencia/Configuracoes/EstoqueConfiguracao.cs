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

public sealed class VendaProdutoConfiguracao : IEntityTypeConfiguration<VendaProduto>
{
    public void Configure(EntityTypeBuilder<VendaProduto> builder)
    {
        builder.ToTable("vendas_produto", tabela =>
            tabela.HasCheckConstraint("ck_vendas_produto_um_vendedor", "(vendedor_profissional_id IS NULL) <> (vendedor_usuario_id IS NULL)"));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.VendedorNome).HasMaxLength(200).IsRequired();
        builder.Property(v => v.Total).HasColumnType("numeric(10,2)");
        builder.Property(v => v.PercentualComissao).HasColumnType("numeric(5,2)");
        builder.Property(v => v.ValorComissao).HasColumnType("numeric(10,2)");
        builder.Property(v => v.MotivoEstorno).HasMaxLength(VendaProduto.TamanhoMaximoMotivoEstorno);
        builder.Ignore(v => v.Estornada);
        // xmin: dois estornos da mesma venda ao mesmo tempo — só o primeiro grava.
        builder.Property<uint>("Versao").IsRowVersion();
        builder.HasMany(v => v.Itens).WithOne().HasForeignKey(i => i.VendaId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Itens).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(v => new { v.NegocioId, v.Data });
        builder.HasIndex(v => v.AgendamentoId);
        builder.HasIndex(v => new { v.VendedorProfissionalId, v.Data });
        builder.HasIndex(v => new { v.VendedorUsuarioId, v.Data });
    }
}

public sealed class ItemVendaProdutoConfiguracao : IEntityTypeConfiguration<ItemVendaProduto>
{
    public void Configure(EntityTypeBuilder<ItemVendaProduto> builder)
    {
        builder.ToTable("itens_venda_produto");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.NomeProduto).HasMaxLength(200).IsRequired();
        builder.Property(i => i.ValorUnitario).HasColumnType("numeric(10,2)");
        builder.Property(i => i.Total).HasColumnType("numeric(10,2)");
        builder.HasOne<Produto>().WithMany().HasForeignKey(i => i.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MovimentoEstoque>().WithMany().HasForeignKey(i => i.MovimentoEstoqueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.ProdutoId);
    }
}
