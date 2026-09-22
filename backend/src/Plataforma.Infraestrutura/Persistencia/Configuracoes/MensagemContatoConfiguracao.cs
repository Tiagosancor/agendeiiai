using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Contato;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class MensagemContatoConfiguracao : IEntityTypeConfiguration<MensagemContato>
{
    public void Configure(EntityTypeBuilder<MensagemContato> builder)
    {
        builder.ToTable("mensagens_contato");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Nome).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Telefone).HasMaxLength(20);
        builder.Property(m => m.Email).HasMaxLength(320);
        builder.Property(m => m.Mensagem).HasMaxLength(4000).IsRequired();
        builder.Property(m => m.CriadoEm).IsRequired();
    }
}
