using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Servicos;

/// <summary>
/// Categoria, nome, preço, duração, indicador "popular" e ativo/inativo (seção 7).
/// Preço/duração por profissional (<c>ProfissionalServico</c>) e o cálculo de horários
/// livres entram na Sprint 2, junto com a agenda.
/// </summary>
public class Servico : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid CategoriaId { get; private set; }

    public string Nome { get; private set; } = string.Empty;

    public decimal Preco { get; private set; }

    public int DuracaoMinutos { get; private set; }

    public bool Popular { get; private set; }

    public bool Ativo { get; private set; } = true;

    protected Servico()
    {
    }

    private Servico(Guid negocioId, Guid categoriaId, string nome, decimal preco, int duracaoMinutos)
    {
        NegocioId = negocioId;
        CategoriaId = categoriaId;
        Nome = nome;
        Preco = preco;
        DuracaoMinutos = duracaoMinutos;
    }

    public static Servico Criar(
        Guid negocioId, Guid categoriaId, string nome, decimal preco, int duracaoMinutos, bool popular = false)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome do serviço é obrigatório.", nameof(nome));

        if (preco < 0)
            throw new ArgumentException("O preço não pode ser negativo.", nameof(preco));

        if (duracaoMinutos <= 0)
            throw new ArgumentException("A duração precisa ser maior que zero.", nameof(duracaoMinutos));

        return new Servico(negocioId, categoriaId, nome.Trim(), preco, duracaoMinutos) { Popular = popular };
    }

    public void AtualizarDados(Guid categoriaId, string nome, decimal preco, int duracaoMinutos, bool popular)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome do serviço é obrigatório.", nameof(nome));

        if (preco < 0)
            throw new ArgumentException("O preço não pode ser negativo.", nameof(preco));

        if (duracaoMinutos <= 0)
            throw new ArgumentException("A duração precisa ser maior que zero.", nameof(duracaoMinutos));

        CategoriaId = categoriaId;
        Nome = nome.Trim();
        Preco = preco;
        DuracaoMinutos = duracaoMinutos;
        Popular = popular;
    }

    public void Desativar() => Ativo = false;

    public void Ativar() => Ativo = true;
}
