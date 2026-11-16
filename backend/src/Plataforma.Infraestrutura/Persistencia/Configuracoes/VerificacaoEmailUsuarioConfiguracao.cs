using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class VerificacaoEmailUsuarioConfiguracao : IEntityTypeConfiguration<VerificacaoEmailUsuario>
{
    public void Configure(EntityTypeBuilder<VerificacaoEmailUsuario> builder)
    {
        builder.ToTable("verificacoes_email_usuario");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Email).HasMaxLength(320);
        builder.Property(v => v.CodigoHash).HasMaxLength(128).IsRequired();
        builder.Property(v => v.ExpiraEm).IsRequired();
        builder.Property(v => v.Tentativas).IsRequired();
        builder.Property(v => v.MaximoTentativas).IsRequired();

        // xmin: duas confirmações simultâneas do mesmo código — só a primeira grava (uso único).
        builder.Property<uint>("Versao").IsRowVersion();

        builder.HasIndex(v => new { v.UsuarioId, v.CriadoEm });

        builder.HasOne<Usuario>().WithMany().HasForeignKey(v => v.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TentativaLoginConfiguracao : IEntityTypeConfiguration<TentativaLogin>
{
    public void Configure(EntityTypeBuilder<TentativaLogin> builder)
    {
        builder.ToTable("tentativas_login");

        // Chave = HMAC-SHA256 em hex do identificador normalizado (ou "conta:{id}" já em HMAC): nunca o identificador.
        builder.HasKey(t => t.Chave);
        builder.Property(t => t.Chave).HasMaxLength(128).ValueGeneratedNever();
        builder.Property(t => t.JanelaInicio).IsRequired();
        builder.Property(t => t.Falhas).IsRequired();
        builder.Property(t => t.AtualizadoEm).IsRequired();

        // A limpeza diária apaga pelo atualizado_em.
        builder.HasIndex(t => t.AtualizadoEm);
    }
}
