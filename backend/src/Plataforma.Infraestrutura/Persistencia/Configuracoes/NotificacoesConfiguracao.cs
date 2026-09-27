using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Notificacoes;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class NotificacaoProfissionalConfiguracao : IEntityTypeConfiguration<NotificacaoProfissional>
{
    public void Configure(EntityTypeBuilder<NotificacaoProfissional> builder)
    {
        builder.ToTable("notificacoes_profissional");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Evento).HasMaxLength(20).IsRequired();
        builder.Property(n => n.CanalEmailStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.CanalWhatsAppStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.IdMensagemWhatsApp).HasMaxLength(200);

        builder.HasIndex(n => n.AgendamentoId);
        builder.HasIndex(n => n.IdMensagemWhatsApp);
    }
}

public sealed class EventoWebhookWhatsAppConfiguracao : IEntityTypeConfiguration<EventoWebhookWhatsApp>
{
    public void Configure(EntityTypeBuilder<EventoWebhookWhatsApp> builder)
    {
        builder.ToTable("eventos_webhook_whatsapp");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provedor).HasMaxLength(40).IsRequired();
        builder.Property(e => e.IdEvento).HasMaxLength(250).IsRequired();
        builder.Property(e => e.IdMensagem).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Idempotência (mesmo padrão da seção 8.6.6): o banco recusa o mesmo evento duas vezes.
        builder.HasIndex(e => new { e.Provedor, e.IdEvento }).IsUnique();
        builder.HasIndex(e => e.IdMensagem);
    }
}
