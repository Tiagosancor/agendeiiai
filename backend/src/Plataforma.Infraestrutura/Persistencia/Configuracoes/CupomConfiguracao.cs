using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Cupons;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class CupomConfiguracao : IEntityTypeConfiguration<Cupom>
{
    public void Configure(EntityTypeBuilder<Cupom> builder)
    {
        builder.ToTable("cupons");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Codigo).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Valor).HasColumnType("numeric(10,2)");

        // Escopo de serviços guardado como lista de ids separada por vírgula — vazio
        // significa "todos os serviços" (seção 7). Backing field privado, mesmo padrão de
        // Negocio.HorarioFuncionamento/Usuario.Permissoes, mas aqui é lista de valor, não
        // de entidade relacionada, por isso Property (com conversão), não HasMany.
        builder.Property<List<Guid>>("_servicoIdsEscopo")
            .HasConversion(
                lista => string.Join(',', lista),
                texto => string.IsNullOrEmpty(texto)
                    ? new List<Guid>()
                    : texto.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList(),
                // Sem isso o EF compara a lista por referência: como AtualizarCondicoes muta
                // a MESMA instância em memória (Clear + AddRange), a detecção de mudança nunca
                // veria diferença entre "antes" e "depois" e o UPDATE perderia essa coluna.
                new ValueComparer<List<Guid>>(
                    (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
                    lista => lista.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
                    lista => lista.ToList()))
            .HasColumnName("servico_ids_escopo")
            .HasMaxLength(4000)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.NegocioId, c.Codigo }).IsUnique();
    }
}
