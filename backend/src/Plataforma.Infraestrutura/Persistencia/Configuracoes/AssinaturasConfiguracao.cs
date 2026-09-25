using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Administracao;
using Plataforma.Dominio.Assinaturas;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class PlanoConfiguracao : IEntityTypeConfiguration<Plano>
{
    /// <summary>IDs fixos dos planos semeados — referenciados pela migration que dá assinatura aos negócios já existentes.</summary>
    public static readonly Guid IdComeco = new("6f1c2a3b-4d5e-4f60-8a71-000000000001");
    public static readonly Guid IdRitmo = new("6f1c2a3b-4d5e-4f60-8a71-000000000002");
    public static readonly Guid IdCasaCheia = new("6f1c2a3b-4d5e-4f60-8a71-000000000003");

    public void Configure(EntityTypeBuilder<Plano> builder)
    {
        builder.ToTable("planos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Nome).HasMaxLength(60).IsRequired();
        builder.Property(p => p.PrecoMensal).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(p => p.PrecoAnualPorMes).HasColumnType("numeric(10,2)").IsRequired();

        // Seed das três faixas (seção 6.4). Preço/nome mudam editando a tabela, sem deploy.
        var criadoEm = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(
            new { Id = IdComeco, Nome = "Começo", MinimoProfissionais = 1, MaximoProfissionais = 3, PrecoMensal = 49.90m, PrecoAnualPorMes = 38.90m, Ordem = 1, Destaque = false, Ativo = true, CriadoEm = criadoEm },
            new { Id = IdRitmo, Nome = "Ritmo", MinimoProfissionais = 4, MaximoProfissionais = 7, PrecoMensal = 79.90m, PrecoAnualPorMes = 68.90m, Ordem = 2, Destaque = true, Ativo = true, CriadoEm = criadoEm },
            new { Id = IdCasaCheia, Nome = "Casa Cheia", MinimoProfissionais = 8, MaximoProfissionais = 12, PrecoMensal = 99.90m, PrecoAnualPorMes = 88.90m, Ordem = 3, Destaque = false, Ativo = true, CriadoEm = criadoEm });
    }
}

public sealed class AssinaturaConfiguracao : IEntityTypeConfiguration<Assinatura>
{
    public void Configure(EntityTypeBuilder<Assinatura> builder)
    {
        builder.ToTable("assinaturas");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Periodicidade).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.PrecoMensalTravado).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(a => a.ProvedorGateway).HasMaxLength(40);
        builder.Property(a => a.IdExternoGateway).HasMaxLength(200);

        // Concorrência otimista pela coluna de sistema xmin do Postgres: job, requisições e
        // administração podem aplicar transições ao mesmo tempo — só uma grava, sem histórico duplicado.
        builder.Property<uint>("Versao").IsRowVersion();

        // Uma assinatura por negócio.
        builder.HasIndex(a => a.NegocioId).IsUnique();
        builder.HasIndex(a => a.Estado);

        builder.HasOne<Plano>().WithMany().HasForeignKey(a => a.PlanoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Historico).WithOne().HasForeignKey(h => h.AssinaturaId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Historico).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(a => a.ValorDoPeriodo);
        builder.Ignore(a => a.PrazoAtual);
        builder.Ignore(a => a.NuncaPagou);
        builder.Ignore(a => a.PermiteOperar);
    }
}

public sealed class HistoricoAssinaturaConfiguracao : IEntityTypeConfiguration<HistoricoAssinatura>
{
    public void Configure(EntityTypeBuilder<HistoricoAssinatura> builder)
    {
        builder.ToTable("historico_assinaturas");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.EstadoAnterior).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.EstadoNovo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.Autor).HasMaxLength(200).IsRequired();
        builder.Property(h => h.Motivo).HasMaxLength(500).IsRequired();
    }
}

public sealed class CobrancaAssinaturaConfiguracao : IEntityTypeConfiguration<CobrancaAssinatura>
{
    public void Configure(EntityTypeBuilder<CobrancaAssinatura> builder)
    {
        builder.ToTable("cobrancas_assinatura");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Valor).HasColumnType("numeric(10,2)").IsRequired();
        builder.Property(c => c.Forma).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Origem).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.IdExterno).HasMaxLength(200);
        builder.Property(c => c.Autor).HasMaxLength(200).IsRequired();

        builder.HasOne<Assinatura>().WithMany().HasForeignKey(c => c.AssinaturaId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.AssinaturaId, c.PagoEm });

        // A mesma cobrança do gateway nunca entra duas vezes.
        builder.HasIndex(c => new { c.Origem, c.IdExterno }).IsUnique().HasFilter("id_externo IS NOT NULL");
    }
}

public sealed class EventoWebhookPagamentoConfiguracao : IEntityTypeConfiguration<EventoWebhookPagamento>
{
    public void Configure(EntityTypeBuilder<EventoWebhookPagamento> builder)
    {
        builder.ToTable("eventos_webhook_pagamento");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provedor).HasMaxLength(40).IsRequired();
        builder.Property(e => e.IdEvento).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Tipo).HasMaxLength(100).IsRequired();

        // Idempotência do webhook (seção 8.6.6): o banco recusa o mesmo evento duas vezes.
        builder.HasIndex(e => new { e.Provedor, e.IdEvento }).IsUnique();
    }
}

public sealed class LogAuditoriaPlataformaConfiguracao : IEntityTypeConfiguration<LogAuditoriaPlataforma>
{
    public void Configure(EntityTypeBuilder<LogAuditoriaPlataforma> builder)
    {
        builder.ToTable("logs_auditoria_plataforma");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Autor).HasMaxLength(320).IsRequired();
        builder.Property(l => l.Acao).HasMaxLength(100).IsRequired();

        builder.HasIndex(l => l.CriadoEm);
        builder.HasIndex(l => l.NegocioId);
    }
}

public sealed class AdministradorPlataformaConfiguracao : IEntityTypeConfiguration<AdministradorPlataforma>
{
    public void Configure(EntityTypeBuilder<AdministradorPlataforma> builder)
    {
        builder.ToTable("administradores_plataforma");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Email).HasMaxLength(320).IsRequired();
        builder.Property(a => a.Nome).HasMaxLength(200).IsRequired();
        builder.Property(a => a.SenhaHash).HasMaxLength(200).IsRequired();

        builder.HasIndex(a => a.Email).IsUnique();
    }
}
