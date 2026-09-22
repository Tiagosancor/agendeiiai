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
        builder.Property(n => n.TituloPagina).HasMaxLength(200);
        builder.Property(n => n.SubtituloPagina).HasMaxLength(300);
        builder.Property(n => n.TextoSobre).HasMaxLength(4000);
        builder.Property(n => n.Telefone).HasMaxLength(20);

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
