using Microsoft.EntityFrameworkCore;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Clientes;

/// <summary>Regra comum do cadastro rápido de cliente do balcão (encaixe e venda).</summary>
internal static class CadastroRapidoDeCliente
{
    /// <summary>
    /// Conflito de unicidade ao gravar o cliente novo: ou o INSERT já tinha sido confirmado (COMMIT ambíguo, B7 — a repetição
    /// bate na chave primária; o cliente certo é o do Id gerado na requisição) ou outra requisição cadastrou o mesmo telefone.
    /// Nunca procura por "telefone nulo": sem telefone, o primeiro cliente sem telefone do negócio não é este.
    /// </summary>
    public static async Task<Guid> RecuperarAposConflitoAsync(
        PlataformaDbContext dbContext, Guid novoId, TelefoneE164? telefone, DbUpdateException conflito, CancellationToken cancellationToken)
    {
        if (await dbContext.Clientes.AsNoTracking().AnyAsync(c => c.Id == novoId, cancellationToken))
            return novoId;
        if (telefone is null)
            throw conflito;
        return (await dbContext.Clientes.AsNoTracking().FirstAsync(c => c.Telefone == telefone, cancellationToken)).Id;
    }
}
