using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Profissionais;

/// <summary>Folga ou bloqueio do profissional por período — horas ou dias (seção 7). Enquanto bloqueado, o horário não é oferecido.</summary>
public class BloqueioAgenda : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid ProfissionalId { get; private set; }

    public DateTimeOffset InicioUtc { get; private set; }

    public DateTimeOffset FimUtc { get; private set; }

    public string? Motivo { get; private set; }

    protected BloqueioAgenda()
    {
    }

    private BloqueioAgenda(Guid negocioId, Guid profissionalId, DateTimeOffset inicioUtc, DateTimeOffset fimUtc, string? motivo)
    {
        NegocioId = negocioId;
        ProfissionalId = profissionalId;
        InicioUtc = inicioUtc;
        FimUtc = fimUtc;
        Motivo = motivo;
    }

    public static BloqueioAgenda Criar(Guid negocioId, Guid profissionalId, DateTimeOffset inicioUtc, DateTimeOffset fimUtc, string? motivo = null)
    {
        if (fimUtc <= inicioUtc)
            throw new ArgumentException("O fim do bloqueio precisa ser depois do início.", nameof(fimUtc));

        return new BloqueioAgenda(negocioId, profissionalId, inicioUtc, fimUtc, motivo);
    }
}
