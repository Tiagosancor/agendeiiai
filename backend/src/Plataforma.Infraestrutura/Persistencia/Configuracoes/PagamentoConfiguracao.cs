using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Financeiro;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class PagamentoConfiguracao : IEntityTypeConfiguration<Pagamento>
{
    public void Configure(EntityTypeBuilder<Pagamento> builder)
    {
        builder.ToTable("pagamentos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Valor).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(p => p.Forma).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Um pagamento por agendamento (seção 7) — checado em GerenciadorPagamentos antes de
        // inserir; o índice único é a garantia real contra corrida (mesmo espírito da seção 8.2).
        builder.HasIndex(p => p.AgendamentoId).IsUnique();
    }
}
