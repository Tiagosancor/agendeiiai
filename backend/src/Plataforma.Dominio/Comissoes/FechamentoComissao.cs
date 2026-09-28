using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Comissoes;

/// <summary>
/// Retrato do acerto de um profissional numa quinzena fechada (seção 7). Gravado ao fechar e
/// nunca alterado depois; reabrir a quinzena apaga os fechamentos dela, e fechar de novo gera
/// outros. Preparado para "marcar como pago" e para vale/consumo (item 12), ainda não usados.
/// </summary>
public class FechamentoComissao : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid PeriodoComissaoId { get; private set; }

    public Guid ProfissionalId { get; private set; }

    public decimal TotalCobrado { get; private set; }

    public decimal TotalComissao { get; private set; }

    public int QuantidadeServicos { get; private set; }

    public Guid? FechadoPorUsuarioId { get; private set; }

    public DateTimeOffset FechadoEm { get; private set; }

    protected FechamentoComissao()
    {
    }

    private FechamentoComissao(Guid negocioId, Guid periodoId, Guid profissionalId)
    {
        NegocioId = negocioId;
        PeriodoComissaoId = periodoId;
        ProfissionalId = profissionalId;
    }

    public static FechamentoComissao Criar(
        Guid negocioId, Guid periodoId, Guid profissionalId, decimal totalCobrado, decimal totalComissao, int quantidadeServicos,
        Guid? fechadoPorUsuarioId, DateTimeOffset fechadoEm) =>
        new(negocioId, periodoId, profissionalId)
        {
            TotalCobrado = totalCobrado,
            TotalComissao = totalComissao,
            QuantidadeServicos = quantidadeServicos,
            FechadoPorUsuarioId = fechadoPorUsuarioId,
            FechadoEm = fechadoEm,
        };
}
