using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Idempotencia;
using Plataforma.Dominio.Negocios;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class RequisicaoIdempotenteConfiguracao : IEntityTypeConfiguration<RequisicaoIdempotente>
{
    public void Configure(EntityTypeBuilder<RequisicaoIdempotente> builder)
    {
        builder.ToTable("requisicoes_idempotentes", tabela =>
            // Resultado mínimo (ids e totais): o limite protege a tabela de virar depósito de respostas.
            tabela.HasCheckConstraint(
                "ck_requisicoes_idempotentes_resultado_tamanho",
                $"octet_length(resultado::text) <= {RequisicaoIdempotente.TamanhoMaximoResultadoBytes}"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Principal).HasMaxLength(80).IsRequired();
        builder.Property(r => r.Escopo).HasMaxLength(60).IsRequired();
        builder.Property(r => r.Chave).HasMaxLength(RequisicaoIdempotente.TamanhoMaximoChave).IsRequired();
        builder.Property(r => r.HashRequisicao).HasMaxLength(64).IsRequired();
        builder.Property(r => r.StatusHttp).IsRequired();
        builder.Property(r => r.Resultado).HasColumnType("jsonb").IsRequired();

        // A garantia real: mesma chave, mesmo negócio, usuário e endpoint = uma operação.
        builder.HasIndex(r => new { r.NegocioId, r.Principal, r.Escopo, r.Chave }).IsUnique();

        // Limpeza diária dos expirados.
        builder.HasIndex(r => r.ExpiraEm);

        builder.HasOne<Negocio>().WithMany().HasForeignKey(r => r.NegocioId).OnDelete(DeleteBehavior.Cascade);
    }
}
