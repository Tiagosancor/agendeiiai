using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Servicos;

public class Categoria : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public string Nome { get; private set; } = string.Empty;

    public bool Ativa { get; private set; } = true;

    protected Categoria()
    {
    }

    private Categoria(Guid negocioId, string nome)
    {
        NegocioId = negocioId;
        Nome = nome;
    }

    public static Categoria Criar(Guid negocioId, string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome da categoria é obrigatório.", nameof(nome));

        return new Categoria(negocioId, nome.Trim());
    }

    public void Renomear(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome da categoria é obrigatório.", nameof(nome));

        Nome = nome.Trim();
    }

    public void Desativar() => Ativa = false;

    public void Ativar() => Ativa = true;
}
