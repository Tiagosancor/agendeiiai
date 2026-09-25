using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Cadastro;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class CodigoCadastroConfiguracao : IEntityTypeConfiguration<CodigoCadastro>
{
    public void Configure(EntityTypeBuilder<CodigoCadastro> builder)
    {
        builder.ToTable("codigos_cadastro");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Email).HasMaxLength(320).IsRequired();
        builder.Property(c => c.HashCodigo).HasMaxLength(128).IsRequired();

        builder.HasIndex(c => new { c.Email, c.CriadoEm });
    }
}

public sealed class RegistroTesteGratisConfiguracao : IEntityTypeConfiguration<RegistroTesteGratis>
{
    public void Configure(EntityTypeBuilder<RegistroTesteGratis> builder)
    {
        builder.ToTable("registros_teste_gratis");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Email).HasMaxLength(320).IsRequired();
        builder.Property(r => r.Telefone).HasMaxLength(20).IsRequired();

        // Um teste por e-mail e um por telefone (seção 8.6.4) — o banco é quem garante.
        builder.HasIndex(r => r.Email).IsUnique();
        builder.HasIndex(r => r.Telefone).IsUnique();
    }
}

public sealed class ChaveIdempotenciaConfiguracao : IEntityTypeConfiguration<ChaveIdempotencia>
{
    public void Configure(EntityTypeBuilder<ChaveIdempotencia> builder)
    {
        builder.ToTable("chaves_idempotencia");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Escopo).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Chave).HasMaxLength(ChaveIdempotencia.TamanhoMaximo).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(320).IsRequired();

        builder.HasIndex(c => new { c.Escopo, c.Chave }).IsUnique();
    }
}
