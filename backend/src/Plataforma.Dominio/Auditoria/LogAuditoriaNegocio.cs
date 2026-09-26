using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Auditoria;

/// <summary>
/// Quem fez o quê, quando, dentro de um negócio (seção 7: toda edição e exclusão de cadastro
/// fica registrada). Mesmo formato do <c>LogAuditoriaPlataforma</c>, mas por negócio e com o
/// filtro multi-tenant — os dois nunca se misturam numa tabela só. Os detalhes guardam só o
/// nome dos campos alterados, nunca os valores de dados pessoais (LGPD).
/// </summary>
public class LogAuditoriaNegocio : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    /// <summary>Nulo quando a ação não veio de um usuário do painel (ex.: sistema).</summary>
    public Guid? AutorUsuarioId { get; private set; }

    public string Autor { get; private set; } = string.Empty;

    public string Acao { get; private set; } = string.Empty;

    public string Entidade { get; private set; } = string.Empty;

    public Guid EntidadeId { get; private set; }

    public string? Detalhes { get; private set; }

    protected LogAuditoriaNegocio()
    {
    }

    public LogAuditoriaNegocio(
        Guid negocioId, Guid? autorUsuarioId, string autor, string acao, string entidade, Guid entidadeId, string? detalhes)
    {
        if (string.IsNullOrWhiteSpace(autor) || string.IsNullOrWhiteSpace(acao) || string.IsNullOrWhiteSpace(entidade))
            throw new ArgumentException("Autor, ação e entidade são obrigatórios.");

        NegocioId = negocioId;
        AutorUsuarioId = autorUsuarioId;
        Autor = autor;
        Acao = acao;
        Entidade = entidade;
        EntidadeId = entidadeId;
        Detalhes = detalhes;
    }
}
