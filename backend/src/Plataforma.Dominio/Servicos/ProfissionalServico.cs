using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Servicos;

/// <summary>Serviço que um profissional executa, com preço/duração próprios opcionais (seção 7).</summary>
public class ProfissionalServico : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid ProfissionalId { get; private set; }

    public Guid ServicoId { get; private set; }

    public decimal? PrecoPersonalizado { get; private set; }

    public int? DuracaoPersonalizadaMinutos { get; private set; }

    protected ProfissionalServico()
    {
    }

    private ProfissionalServico(Guid negocioId, Guid profissionalId, Guid servicoId)
    {
        NegocioId = negocioId;
        ProfissionalId = profissionalId;
        ServicoId = servicoId;
    }

    public static ProfissionalServico Criar(
        Guid negocioId, Guid profissionalId, Guid servicoId, decimal? precoPersonalizado = null, int? duracaoPersonalizadaMinutos = null)
    {
        if (precoPersonalizado is < 0)
            throw new ArgumentException("O preço não pode ser negativo.", nameof(precoPersonalizado));

        if (duracaoPersonalizadaMinutos is <= 0)
            throw new ArgumentException("A duração precisa ser maior que zero.", nameof(duracaoPersonalizadaMinutos));

        return new ProfissionalServico(negocioId, profissionalId, servicoId)
        {
            PrecoPersonalizado = precoPersonalizado,
            DuracaoPersonalizadaMinutos = duracaoPersonalizadaMinutos,
        };
    }
}
