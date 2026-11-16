using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class UsuarioConfiguracao : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Nome).HasMaxLength(200).IsRequired();
        // Anulável: quem entra por nome de usuário ou telefone pode não ter e-mail. Os excluídos ainda guardam "" até a
        // migration de limpeza (a versão anterior do código materializa a coluna como não anulável).
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.EmailVerificadoEm);

        // Reconstituir (não Criar) ao ler: um nome gravado antes de virar reservado não pode derrubar a leitura.
        builder.Property(u => u.NomeUsuario)
            .HasConversion(nome => nome!.Valor, valor => NomeUsuario.Reconstituir(valor))
            .HasMaxLength(NomeUsuario.TamanhoMaximo);

        // Sempre na forma canônica (13 dígitos): o índice único também impede o mesmo celular com e sem o 9º dígito.
        builder.Property(u => u.TelefoneLogin)
            .HasConversion(telefone => telefone!.Valor, valor => TelefoneE164.Criar(valor))
            .HasMaxLength(20);

        builder.Property(u => u.SenhaDefinidaPorTerceiro).HasDefaultValue(false).IsRequired();
        builder.Property(u => u.Telefone).HasMaxLength(20);
        builder.Property(u => u.SenhaHash).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Perfil).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(u => u.Ativo).IsRequired();
        builder.Property(u => u.PercentualComissaoProdutoVenda).HasColumnType("numeric(5,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(u => u.AcertoPorQuinzena).HasDefaultValue(false).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_usuarios_percentual_comissao_produto_venda", "percentual_comissao_produto_venda >= 0 AND percentual_comissao_produto_venda <= 100"));

        // Único GLOBAL (não por negócio): o login acontece num único domínio compartilhado
        // (app.{dominio} — seção 5), então o e-mail sozinho precisa apontar pro usuário
        // certo antes de qualquer tenant estar resolvido. Ver docs/decisoes.md.
        // Só entre os não excluídos (seção 7): o e-mail de quem foi excluído pode voltar a ser
        // usado, e os excluídos com histórico ficam todos com e-mail vazio.
        // Vazio ("") é o que a versão anterior grava nos excluídos; nulo é o que a nova grava (e os sem e-mail).
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("excluido = false AND email IS NOT NULL AND email <> ''");

        // Nome de usuário e telefone de login: únicos no sistema inteiro, só entre os não excluídos (o inativo os mantém,
        // para poder ser reativado). A checagem prévia é da aplicação; o índice decide a corrida.
        builder.HasIndex(u => u.NomeUsuario).IsUnique().HasFilter("excluido = false AND nome_usuario IS NOT NULL");
        builder.HasIndex(u => u.TelefoneLogin).IsUnique().HasFilter("excluido = false AND telefone_login IS NOT NULL");

        // Pelo menos um jeito de entrar. A versão antiga passa: grava e-mail nos ativos e só zera nos excluídos.
        // O formato do nome de usuário é só defesa em profundidade (a regra completa é do domínio).
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_usuarios_algum_identificador",
                "excluido OR coalesce(email, '') <> '' OR nome_usuario IS NOT NULL OR telefone_login IS NOT NULL");
            t.HasCheckConstraint("ck_usuarios_nome_usuario_formato",
                "nome_usuario IS NULL OR (nome_usuario ~ '^[a-z0-9][a-z0-9._-]{1,28}[a-z0-9]$' AND nome_usuario ~ '[a-z]' AND nome_usuario !~ '[._-]{2}')");
        });

        // Vínculo com o profissional da agenda (seção 7): um profissional tem no máximo um usuário, e um
        // usuário no máximo um profissional (é uma coluna só). O excluído já perde o vínculo.
        builder.HasIndex(u => u.ProfissionalId).IsUnique().HasFilter("profissional_id IS NOT NULL")
            .HasDatabaseName(Profissionais.GerenciadorProfissionais.IndiceUsuarioPorProfissional);

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
