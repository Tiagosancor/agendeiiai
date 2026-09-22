using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class ClienteConfiguracao : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("clientes");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Nome).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(320);
        builder.Property(c => c.Observacoes).HasMaxLength(2000);
        builder.Property(c => c.Origem).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(c => c.Telefone)
            .HasConversion(telefone => telefone.Valor, valor => TelefoneE164.Criar(valor))
            .HasColumnName("telefone")
            .HasMaxLength(20)
            .IsRequired();

        // Chave de identificação do cliente dentro do negócio (seção 7 / 8.1.4).
        builder.HasIndex(c => new { c.NegocioId, c.Telefone }).IsUnique();
    }
}
