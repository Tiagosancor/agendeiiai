using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Negocios;
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

public sealed class NotificacaoPendenteConfiguracao : IEntityTypeConfiguration<NotificacaoPendente>
{
    public void Configure(EntityTypeBuilder<NotificacaoPendente> builder)
    {
        builder.ToTable("notificacoes_pendentes", tabela =>
        {
            tabela.HasCheckConstraint("ck_notificacoes_pendentes_tentativas", "tentativas >= 0");
            // Reivindicação e lease andam juntos: só um item "Enviando" tem os dois.
            tabela.HasCheckConstraint(
                "ck_notificacoes_pendentes_lease",
                "(status = 'Enviando' AND lease_ate IS NOT NULL AND reivindicacao_id IS NOT NULL) OR (status <> 'Enviando' AND reivindicacao_id IS NULL)");
        });

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Tipo).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(n => n.Canal).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.ChaveDeduplicacao).HasMaxLength(200).IsRequired();
        builder.Property(n => n.UltimoErroCodigo).HasMaxLength(60);
        builder.Property(n => n.IdMensagemProvedor).HasMaxLength(200);

        // Deduplicação por canal e por tenant: a mesma chave técnica em negócios diferentes não colide.
        builder.HasIndex(n => new { n.NegocioId, n.ChaveDeduplicacao, n.Canal }).IsUnique().HasDatabaseName("ux_notificacoes_pendentes_dedup");

        // Caminho quente do despachante: o que está pronto para sair, na ordem. Parciais — a tabela cresce com o histórico.
        builder.HasIndex(n => new { n.ProximaTentativaEm, n.Id }).HasDatabaseName("ix_notificacoes_pendentes_prontas").HasFilter("status = 'Pendente'");
        builder.HasIndex(n => n.LeaseAte).HasDatabaseName("ix_notificacoes_pendentes_lease").HasFilter("status = 'Enviando'");

        // Contrato administrativo (filtro por status/período) e retenção.
        builder.HasIndex(n => new { n.Status, n.AtualizadoEm }).HasDatabaseName("ix_notificacoes_pendentes_status_atualizada");
        builder.HasIndex(n => n.AgendamentoId).HasDatabaseName("ix_notificacoes_pendentes_agendamento");
        builder.HasIndex(n => n.IdMensagemProvedor).HasDatabaseName("ix_notificacoes_pendentes_id_mensagem").HasFilter("id_mensagem_provedor IS NOT NULL");

        // Sem FK para agendamento/cliente/profissional: o item precisa sobreviver à anonimização e a limpezas, e a outbox é gravada
        // antes do SaveChanges do agendamento na mesma transação. O negócio, sim, é dono do item.
        builder.HasOne<Negocio>().WithMany().HasForeignKey(n => n.NegocioId).OnDelete(DeleteBehavior.Cascade);
    }
}
