using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Assinaturas;

/// <summary>
/// Faixa de preço por número de profissionais ativos (seção 7). Planos são DADOS: mudar preço
/// ou nome é editar a tabela, nunca deploy. Todos têm os mesmos recursos — só o limite muda.
/// </summary>
public class Plano : EntidadeBase
{
    public string Nome { get; private set; } = string.Empty;

    public int MinimoProfissionais { get; private set; }

    public int MaximoProfissionais { get; private set; }

    public decimal PrecoMensal { get; private set; }

    /// <summary>Valor POR MÊS no plano anual (o total cobrado é 12×).</summary>
    public decimal PrecoAnualPorMes { get; private set; }

    /// <summary>Ordem de exibição nos cards do site.</summary>
    public int Ordem { get; private set; }

    /// <summary>Card destacado como "mais escolhido" no site.</summary>
    public bool Destaque { get; private set; }

    public bool Ativo { get; private set; } = true;

    protected Plano()
    {
    }

    public static Plano Criar(
        string nome, int minimoProfissionais, int maximoProfissionais, decimal precoMensal,
        decimal precoAnualPorMes, int ordem = 0, bool destaque = false)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome do plano é obrigatório.", nameof(nome));

        if (minimoProfissionais < 1 || maximoProfissionais < minimoProfissionais)
            throw new ArgumentException("Faixa de profissionais inválida.", nameof(maximoProfissionais));

        if (precoMensal <= 0 || precoAnualPorMes <= 0)
            throw new ArgumentException("Os preços precisam ser maiores que zero.", nameof(precoMensal));

        return new Plano
        {
            Nome = nome.Trim(),
            MinimoProfissionais = minimoProfissionais,
            MaximoProfissionais = maximoProfissionais,
            PrecoMensal = precoMensal,
            PrecoAnualPorMes = precoAnualPorMes,
            Ordem = ordem,
            Destaque = destaque,
        };
    }

    public decimal PrecoPorMes(Periodicidade periodicidade) =>
        periodicidade == Periodicidade.Anual ? PrecoAnualPorMes : PrecoMensal;

    public bool Comporta(int profissionaisAtivos) => profissionaisAtivos <= MaximoProfissionais;

    public void Desativar() => Ativo = false;
}
