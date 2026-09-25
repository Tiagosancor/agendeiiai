using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Administracao;

/// <summary>Toda ação da administração da plataforma (seção 7/8.6.7) — única área que enxerga vários negócios.</summary>
public class LogAuditoriaPlataforma : EntidadeBase
{
    public string Autor { get; private set; } = string.Empty;

    public string Acao { get; private set; } = string.Empty;

    public Guid? NegocioId { get; private set; }

    public string? Detalhes { get; private set; }

    protected LogAuditoriaPlataforma()
    {
    }

    public LogAuditoriaPlataforma(string autor, string acao, Guid? negocioId, string? detalhes)
    {
        if (string.IsNullOrWhiteSpace(autor) || string.IsNullOrWhiteSpace(acao))
            throw new ArgumentException("Autor e ação são obrigatórios.");

        Autor = autor;
        Acao = acao;
        NegocioId = negocioId;
        Detalhes = detalhes;
    }
}
