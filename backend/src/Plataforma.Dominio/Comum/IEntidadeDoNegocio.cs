namespace Plataforma.Dominio.Comum;

/// <summary>
/// Marca uma entidade como pertencente a um negócio (tenant). Toda entidade que
/// implementa esta interface recebe automaticamente o *global query filter* por
/// <c>NegocioId</c> configurado no <c>PlataformaDbContext</c> — ver seção 8.3 do
/// prompt de especificação. Nunca consulte estas entidades ignorando o filtro
/// fora de rotinas administrativas explícitas (ex.: jobs de manutenção).
/// </summary>
public interface IEntidadeDoNegocio
{
    Guid NegocioId { get; }
}
