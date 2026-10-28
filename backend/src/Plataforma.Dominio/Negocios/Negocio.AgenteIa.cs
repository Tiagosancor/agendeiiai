namespace Plataforma.Dominio.Negocios;

/// <summary>
/// Serviço premium: agente de IA do WhatsApp (contratação por negócio). <see cref="AgenteIaContratado"/> é o ÚNICO dado que o
/// runtime do agente consulta; quem o escreve (hoje só o administrador da plataforma) fica em <c>ServicoContratacaoAgenteIa</c>.
/// </summary>
public partial class Negocio
{
    /// <summary>Padrão falso. Sem contratação nada da IA roda para o negócio.</summary>
    public bool AgenteIaContratado { get; private set; }

    /// <summary>Nulo quando desligado.</summary>
    public OrigemAgenteIa? AgenteIaOrigem { get; private set; }

    public DateTimeOffset? AgenteIaAlteradoEm { get; private set; }

    /// <summary>Autor no formato de <c>LogAuditoriaPlataforma.Autor</c>; o histórico completo é o log de auditoria.</summary>
    public string? AgenteIaAlteradoPor { get; private set; }

    /// <summary>Liga o serviço. Devolve falso (e não muda nada) se já estava ligado.</summary>
    public bool LigarAgenteIa(OrigemAgenteIa origem, string autor, DateTimeOffset agora)
    {
        if (!Enum.IsDefined(origem))
            throw new ArgumentOutOfRangeException(nameof(origem));
        if (string.IsNullOrWhiteSpace(autor))
            throw new ArgumentException("O autor é obrigatório.", nameof(autor));

        if (AgenteIaContratado)
            return false;

        AgenteIaContratado = true;
        AgenteIaOrigem = origem;
        AgenteIaAlteradoEm = agora;
        AgenteIaAlteradoPor = autor.Trim();
        return true;
    }

    /// <summary>Desliga o serviço (origem volta a nulo). Devolve falso se já estava desligado.</summary>
    public bool DesligarAgenteIa(string autor, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(autor))
            throw new ArgumentException("O autor é obrigatório.", nameof(autor));

        if (!AgenteIaContratado)
            return false;

        AgenteIaContratado = false;
        AgenteIaOrigem = null;
        AgenteIaAlteradoEm = agora;
        AgenteIaAlteradoPor = autor.Trim();
        return true;
    }
}
