using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Plataforma.Dominio.Fidelidade;

namespace Plataforma.Infraestrutura.Persistencia.Configuracoes;

public sealed class SeloClienteConfiguracao : IEntityTypeConfiguration<SeloCliente>
{
    public void Configure(EntityTypeBuilder<SeloCliente> builder)
    {
        builder.ToTable("selos_cliente");

        builder.HasKey(s => s.Id);

        // Um selo por agendamento (RegistrarSeloAsync também checa isso antes de inserir).
        builder.HasIndex(s => s.AgendamentoId).IsUnique();

        // Apoia a consulta de progresso (selos não resgatados de um cliente).
        builder.HasIndex(s => new { s.ClienteId, s.Resgatado });
    }
}
