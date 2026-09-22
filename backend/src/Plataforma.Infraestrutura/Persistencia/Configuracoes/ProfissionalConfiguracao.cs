using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Profissionais;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class ProfissionalConfiguracao : IEntityTypeConfiguration<Profissional>
{
    public void Configure(EntityTypeBuilder<Profissional> builder)
    {
        builder.ToTable("profissionais");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Nome).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Telefone).HasMaxLength(20);
        builder.Property(p => p.Email).HasMaxLength(320);
        builder.Property(p => p.Funcao).HasMaxLength(100);
        builder.Property(p => p.Ativo).IsRequired();

        builder.OwnsOne(p => p.Endereco, endereco =>
        {
            endereco.Property(e => e.Bairro).HasColumnName("endereco_bairro").HasMaxLength(120);
            endereco.Property(e => e.Cidade).HasColumnName("endereco_cidade").HasMaxLength(120);
            endereco.Property(e => e.Rua).HasColumnName("endereco_rua").HasMaxLength(200);
            endereco.Property(e => e.Numero).HasColumnName("endereco_numero").HasMaxLength(20);
            endereco.Property(e => e.Cep).HasColumnName("endereco_cep").HasMaxLength(9);
        });

        builder.OwnsOne(p => p.Cpf, cpf =>
        {
            cpf.Property(c => c.Mascarado).HasColumnName("cpf_mascarado").HasMaxLength(20);
            cpf.Property(c => c.TextoCifrado).HasColumnName("cpf_texto_cifrado");
            cpf.Property(c => c.Nonce).HasColumnName("cpf_nonce");
            cpf.Property(c => c.ChaveId).HasColumnName("cpf_chave_id").HasMaxLength(50);
        });
    }
}
