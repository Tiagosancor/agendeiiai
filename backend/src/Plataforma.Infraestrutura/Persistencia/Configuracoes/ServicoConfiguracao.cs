using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Servicos;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class ServicoConfiguracao : IEntityTypeConfiguration<Servico>
{
    public void Configure(EntityTypeBuilder<Servico> builder)
    {
        builder.ToTable("servicos");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Nome).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Preco).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(s => s.DuracaoMinutos).IsRequired();
        builder.Property(s => s.Popular).IsRequired();
        // Sem HasDefaultValue: o EF deixaria de mandar "false" (valor padrão do bool) e o banco gravaria true. O default
        // true para os serviços que já existem fica só na migration.
        builder.Property(s => s.ExibirNaPaginaInicial).IsRequired();
        builder.Property(s => s.Ativo).IsRequired();

        builder.HasOne<Categoria>()
            .WithMany()
            .HasForeignKey(s => s.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
