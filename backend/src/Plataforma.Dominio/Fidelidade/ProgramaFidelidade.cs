using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Fidelidade;

/// <summary>Configuração do cartão de selos por negócio (seção 7) — a cada N atendimentos concluídos, uma recompensa configurável. No máximo um por negócio.</summary>
public class ProgramaFidelidade : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public int SelosNecessarios { get; private set; }

    public string DescricaoRecompensa { get; private set; } = string.Empty;

    public bool Ativo { get; private set; }

    protected ProgramaFidelidade()
    {
    }

    private ProgramaFidelidade(Guid negocioId, int selosNecessarios, string descricaoRecompensa)
    {
        NegocioId = negocioId;
        SelosNecessarios = selosNecessarios;
        DescricaoRecompensa = descricaoRecompensa;
        Ativo = true;
    }

    public static ProgramaFidelidade Criar(Guid negocioId, int selosNecessarios, string descricaoRecompensa)
    {
        if (selosNecessarios <= 0)
            throw new ArgumentException("O número de selos necessários precisa ser maior que zero.", nameof(selosNecessarios));

        if (string.IsNullOrWhiteSpace(descricaoRecompensa))
            throw new ArgumentException("A descrição da recompensa é obrigatória.", nameof(descricaoRecompensa));

        return new ProgramaFidelidade(negocioId, selosNecessarios, descricaoRecompensa.Trim());
    }

    public void AtualizarCondicoes(int selosNecessarios, string descricaoRecompensa)
    {
        if (selosNecessarios <= 0)
            throw new ArgumentException("O número de selos necessários precisa ser maior que zero.", nameof(selosNecessarios));

        if (string.IsNullOrWhiteSpace(descricaoRecompensa))
            throw new ArgumentException("A descrição da recompensa é obrigatória.", nameof(descricaoRecompensa));

        SelosNecessarios = selosNecessarios;
        DescricaoRecompensa = descricaoRecompensa.Trim();
    }

    public void Desativar() => Ativo = false;

    public void Ativar() => Ativo = true;
}
