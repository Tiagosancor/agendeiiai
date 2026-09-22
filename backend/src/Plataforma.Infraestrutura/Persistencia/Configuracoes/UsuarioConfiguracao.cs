using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class UsuarioConfiguracao : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Nome).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.Telefone).HasMaxLength(20);
        builder.Property(u => u.SenhaHash).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Perfil).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(u => u.Ativo).IsRequired();

        // Único GLOBAL (não por negócio): o login acontece num único domínio compartilhado
        // (app.{dominio} — seção 5), então o e-mail sozinho precisa apontar pro usuário
        // certo antes de qualquer tenant estar resolvido. Ver docs/decisoes.md.
        builder.HasIndex(u => u.Email).IsUnique();

        builder.OwnsOne(u => u.Endereco, endereco =>
        {
            endereco.Property(e => e.Bairro).HasColumnName("endereco_bairro").HasMaxLength(120);
            endereco.Property(e => e.Cidade).HasColumnName("endereco_cidade").HasMaxLength(120);
            endereco.Property(e => e.Rua).HasColumnName("endereco_rua").HasMaxLength(200);
            endereco.Property(e => e.Numero).HasColumnName("endereco_numero").HasMaxLength(20);
            endereco.Property(e => e.Cep).HasColumnName("endereco_cep").HasMaxLength(9);
        });

        builder.OwnsOne(u => u.Cpf, cpf =>
        {
            cpf.Property(c => c.Mascarado).HasColumnName("cpf_mascarado").HasMaxLength(20);
            cpf.Property(c => c.TextoCifrado).HasColumnName("cpf_texto_cifrado");
            cpf.Property(c => c.Nonce).HasColumnName("cpf_nonce");
            cpf.Property(c => c.ChaveId).HasColumnName("cpf_chave_id").HasMaxLength(50);
        });

        builder.HasMany(u => u.Permissoes)
            .WithOne()
            .HasForeignKey(p => p.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        // Permissoes é exposta só como IReadOnlyCollection (encapsulamento — seção "Convenções"
        // do CLAUDE.md sobre entidades ricas); o EF lê/escreve direto no campo _permissoes.
        builder.Navigation(u => u.Permissoes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
