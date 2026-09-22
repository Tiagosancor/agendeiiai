using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Verificacao;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class CodigoVerificacaoConfiguracao : IEntityTypeConfiguration<CodigoVerificacao>
{
    public void Configure(EntityTypeBuilder<CodigoVerificacao> builder)
    {
        builder.ToTable("codigos_verificacao");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.HashCodigo).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ExpiraEm).IsRequired();

        builder.Property(c => c.Telefone)
            .HasConversion(telefone => telefone.Valor, valor => TelefoneE164.Criar(valor))
            .HasColumnName("telefone")
            .HasMaxLength(20)
            .IsRequired();

        // Apoia as checagens de rate limit (por telefone/janela de tempo — seção 8.1.5) e a
        // busca do código mais recente na validação (seção 8.1.1.c).
        builder.HasIndex(c => new { c.NegocioId, c.Telefone, c.CriadoEm });
    }
}
