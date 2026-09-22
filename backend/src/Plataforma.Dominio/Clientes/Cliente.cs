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

    /// <summary>LGPD (seção 8.4) — exclusão sob demanda é anonimização, não remoção da linha: preserva o histórico de agendamentos/faturamento sem identificar a pessoa.</summary>
    public bool Excluido { get; private set; }

    public DateTimeOffset? ExcluidoEm { get; private set; }

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

    /// <summary>
    /// Anonimiza em vez de apagar a linha (seção 8.4): zera nome/e-mail/observações e troca
    /// o telefone por um valor sintético (só pra continuar batendo o formato E.164 e a
    /// unicidade — nunca mais identifica nem contata a pessoa de verdade). Os agendamentos
    /// já feitos continuam existindo (auditoria/financeiro), só deixam de apontar pra um
    /// cliente identificável.
    /// </summary>
    public void Anonimizar(DateTimeOffset agora)
    {
        if (Excluido)
            return;

        Nome = "Cliente removido";
        Email = null;
        Observacoes = null;
        Telefone = TelefoneAnonimo(Id);
        Excluido = true;
        ExcluidoEm = agora;
    }

    private static TelefoneE164 TelefoneAnonimo(Guid id)
    {
        var numerico = (uint)id.GetHashCode() % 1_000_000_000;
        return TelefoneE164.Criar($"+19{numerico:D9}");
    }
}
