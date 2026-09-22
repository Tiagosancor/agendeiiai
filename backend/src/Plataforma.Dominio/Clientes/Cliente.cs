using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Clientes;

/// <summary>
/// Ficha do cliente final (seção 7). O telefone E.164 é a chave de identificação dentro
/// do negócio (índice único em <c>(NegocioId, Telefone)</c> — seção 8.1.4); histórico de
/// atendimentos vem de <c>Agendamento</c> (Sprint 2/3), não é guardado aqui.
/// </summary>
public class Cliente : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public string Nome { get; private set; } = string.Empty;

    public TelefoneE164 Telefone { get; private set; } = null!;

    public string? Email { get; private set; }

    public string? Observacoes { get; private set; }

    public OrigemCliente Origem { get; private set; }

    protected Cliente()
    {
    }

    private Cliente(Guid negocioId, string nome, TelefoneE164 telefone, OrigemCliente origem)
    {
        NegocioId = negocioId;
        Nome = nome;
        Telefone = telefone;
        Origem = origem;
    }

    public static Cliente Criar(
        Guid negocioId, string nome, TelefoneE164 telefone, OrigemCliente origem = OrigemCliente.Painel,
        string? email = null, string? observacoes = null)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome é obrigatório.", nameof(nome));

        ArgumentNullException.ThrowIfNull(telefone);

        return new Cliente(negocioId, nome.Trim(), telefone, origem)
        {
            Email = email,
            Observacoes = observacoes,
        };
    }

    /// <summary>
    /// Preenche só o que estiver vazio — nunca sobrescreve dado já existente. Usado pela
    /// identificação automática do agendamento público (seção 8.1.4.a — Sprint 3).
    /// </summary>
    public void PreencherEmailSeVazio(string? email)
    {
        if (string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(email))
            Email = email;
    }

    public void AtualizarDados(string nome, string? email, string? observacoes)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome é obrigatório.", nameof(nome));

        Nome = nome.Trim();
        Email = email;
        Observacoes = observacoes;
    }
}
