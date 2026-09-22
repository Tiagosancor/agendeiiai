using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Profissionais;

/// <summary>
/// Um intervalo de trabalho do profissional num dia da semana (seção 7). Um dia com
/// almoço vira dois registros (ex.: 08:00–12:00 e 13:00–18:00 de segunda) — o buraco
/// entre eles é o almoço, sem precisar de um conceito separado.
/// </summary>
public class HorarioTrabalho : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid ProfissionalId { get; private set; }

    public DiaSemana DiaSemana { get; private set; }

    public TimeOnly Inicio { get; private set; }

    public TimeOnly Fim { get; private set; }

    protected HorarioTrabalho()
    {
    }

    private HorarioTrabalho(Guid negocioId, Guid profissionalId, DiaSemana diaSemana, TimeOnly inicio, TimeOnly fim)
    {
        NegocioId = negocioId;
        ProfissionalId = profissionalId;
        DiaSemana = diaSemana;
        Inicio = inicio;
        Fim = fim;
    }

    public static HorarioTrabalho Criar(Guid negocioId, Guid profissionalId, DiaSemana diaSemana, TimeOnly inicio, TimeOnly fim)
    {
        if (fim <= inicio)
            throw new ArgumentException("O fim do intervalo precisa ser depois do início.", nameof(fim));

        return new HorarioTrabalho(negocioId, profissionalId, diaSemana, inicio, fim);
    }
}
