using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.WhatsAppAgente;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class InstanciaWhatsAppConfiguracao : IEntityTypeConfiguration<InstanciaWhatsApp>
{
    public void Configure(EntityTypeBuilder<InstanciaWhatsApp> builder)
    {
        builder.ToTable("instancias_whatsapp");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.NomeInstancia).HasMaxLength(100).IsRequired();
        builder.Property(i => i.Telefone).HasMaxLength(20);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.MensagemBoasVindas).HasMaxLength(1000);
        builder.Property(i => i.HorarioAtendimentoJson).HasColumnType("jsonb");
        builder.Property(i => i.MensagemTransferencia).HasMaxLength(1000);

        builder.HasOne<Negocio>().WithMany().HasForeignKey(i => i.NegocioId).OnDelete(DeleteBehavior.Cascade);

        // O webhook acha o negócio pelo nome (global); um negócio tem uma instância.
        builder.HasIndex(i => i.NomeInstancia).IsUnique();
        builder.HasIndex(i => i.NegocioId).IsUnique();
    }
}

public sealed class ConversaWhatsAppConfiguracao : IEntityTypeConfiguration<ConversaWhatsApp>
{
    public void Configure(EntityTypeBuilder<ConversaWhatsApp> builder)
    {
        builder.ToTable("conversas_whatsapp");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.TelefoneCliente).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Resumo).HasMaxLength(4000);

        builder.HasOne<InstanciaWhatsApp>().WithMany().HasForeignKey(c => c.InstanciaId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.InstanciaId, c.TelefoneCliente }).IsUnique();
        builder.HasIndex(c => new { c.NegocioId, c.UltimaMensagemEm });
    }
}

public sealed class MensagemWhatsAppConfiguracao : IEntityTypeConfiguration<MensagemWhatsApp>
{
    public void Configure(EntityTypeBuilder<MensagemWhatsApp> builder)
    {
        builder.ToTable("mensagens_whatsapp");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.IdExterno).HasMaxLength(200);
        builder.Property(m => m.Papel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Conteudo).IsRequired();
        builder.Property(m => m.NomeFerramenta).HasMaxLength(100);

        builder.HasOne<ConversaWhatsApp>().WithMany().HasForeignKey(m => m.ConversaId).OnDelete(DeleteBehavior.Cascade);

        // Segunda barreira de deduplicação (a primeira é o Redis). NULL (mensagem sem ID do provedor) não conflita.
        builder.HasIndex(m => new { m.InstanciaId, m.IdExterno }).IsUnique();
        builder.HasIndex(m => new { m.ConversaId, m.CriadoEm });
    }
}
