using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Negocios;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class NegocioConfiguracao : IEntityTypeConfiguration<Negocio>
{
    public void Configure(EntityTypeBuilder<Negocio> builder)
    {
        builder.ToTable("negocios");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Slug)
            .HasConversion(slug => slug.Valor, valor => Slug.Criar(valor))
            .HasMaxLength(Slug.TamanhoMaximo)
            .IsRequired();

        builder.HasIndex(n => n.Slug)
            .IsUnique();

        builder.Property(n => n.NomeExibido)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(n => n.Tipo)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(n => n.Fuso)
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(n => n.Ativo)
            .IsRequired();

        builder.Property(n => n.CriadoEm)
            .IsRequired();

        // Perfil do negócio (marca, textos, endereço, contato — seção 5 / Sprint 1).
        builder.Property(n => n.LogoUrl).HasMaxLength(500);
        builder.Property(n => n.CorPrimaria).HasMaxLength(20);
        builder.Property(n => n.CorSecundaria).HasMaxLength(20);
        builder.Property(n => n.CorFundo).HasMaxLength(7);
        builder.Property(n => n.ImagemFundo).HasMaxLength(64);
        builder.Property(n => n.TituloPagina).HasMaxLength(200);
        builder.Property(n => n.SubtituloPagina).HasMaxLength(300);
        builder.Property(n => n.TextoSobre).HasMaxLength(4000);
        builder.Property(n => n.Telefone).HasMaxLength(20);
        builder.Property(n => n.EmailContato).HasMaxLength(320);
        builder.Property(n => n.WhatsAppAtivoParaConfirmacoes).IsRequired().HasDefaultValue(false);
        builder.Property(n => n.WhatsAppAvisoProfissional).IsRequired().HasDefaultValue(false);

        // Lembrete de retorno: os padrões dos negócios que já existiam ficam só na migration (DEFAULT da coluna) —
        // HasDefaultValue no modelo faria o EF omitir do INSERT um valor igual ao padrão do CLR.
        builder.Property(n => n.LembreteRetornoAtivo).IsRequired();
        builder.Property(n => n.LembreteRetornoDias).IsRequired();
        builder.Property(n => n.LembreteRetornoHorario).IsRequired();
        builder.Property(n => n.LembreteRetornoMensagem).HasMaxLength(LembreteRetorno.TamanhoMaximoMensagem).IsRequired();

        // Agente de IA do WhatsApp (serviço premium): o DEFAULT false fica só na coluna da migration, sem HasDefaultValue no modelo
        // (senão o EF omite do INSERT um valor igual ao padrão do CLR). A origem é gravada como texto; o check no banco fecha a lista.
        builder.Property(n => n.AgenteIaContratado).IsRequired();
        builder.Property(n => n.AgenteIaOrigem).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.AgenteIaAlteradoPor).HasMaxLength(320);
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_negocios_agente_ia_origem", "agente_ia_origem IS NULL OR agente_ia_origem IN ('Manual', 'Plano', 'Adicional')"));

        builder.OwnsOne(n => n.Endereco, endereco =>
        {
            endereco.Property(e => e.Bairro).HasColumnName("endereco_bairro").HasMaxLength(120);
            endereco.Property(e => e.Cidade).HasColumnName("endereco_cidade").HasMaxLength(120);
            endereco.Property(e => e.Rua).HasColumnName("endereco_rua").HasMaxLength(200);
            endereco.Property(e => e.Numero).HasColumnName("endereco_numero").HasMaxLength(20);
            endereco.Property(e => e.Cep).HasColumnName("endereco_cep").HasMaxLength(9);
        });

        builder.OwnsOne(n => n.RedesSociais, redes =>
        {
            redes.Property(r => r.Instagram).HasColumnName("rede_social_instagram").HasMaxLength(200);
            redes.Property(r => r.Facebook).HasColumnName("rede_social_facebook").HasMaxLength(200);
            redes.Property(r => r.WhatsApp).HasColumnName("rede_social_whatsapp").HasMaxLength(30);
        });

        builder.OwnsMany(n => n.HorarioFuncionamento, horario =>
        {
            horario.ToTable("negocio_horario_funcionamento");
            horario.WithOwner().HasForeignKey("NegocioId");
            horario.HasKey("NegocioId", nameof(HorarioFuncionamentoDia.DiaSemana));

            horario.Property(h => h.DiaSemana).HasConversion<string>().HasMaxLength(15);
            horario.Property(h => h.Abertura);
            horario.Property(h => h.Fechamento);
            horario.Property(h => h.Fechado).IsRequired();
        });

        builder.Navigation(n => n.HorarioFuncionamento).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
