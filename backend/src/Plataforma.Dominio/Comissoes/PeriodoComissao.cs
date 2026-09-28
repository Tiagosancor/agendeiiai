using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Comissoes;

public enum EstadoPeriodoComissao
{
    Aberta = 1,
    Fechada = 2,
}

/// <summary>
/// Quinzena de acerto de comissões (seção 7), com datas escolhidas pelo Administrador. Dias
/// inteiros no fuso do negócio (do início às 00:00 ao fim às 23:59:59). A não sobreposição entre
/// períodos do mesmo negócio é garantida no banco (exclusion constraint), não aqui.
/// </summary>
public class PeriodoComissao : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public DateOnly Inicio { get; private set; }

    public DateOnly Fim { get; private set; }

    public EstadoPeriodoComissao Estado { get; private set; } = EstadoPeriodoComissao.Aberta;

    public DateTimeOffset? FechadoEm { get; private set; }

    public Guid? FechadoPorUsuarioId { get; private set; }

    protected PeriodoComissao()
    {
    }

    private PeriodoComissao(Guid negocioId, DateOnly inicio, DateOnly fim)
    {
        NegocioId = negocioId;
        Inicio = inicio;
        Fim = fim;
    }

    public static PeriodoComissao Criar(Guid negocioId, DateOnly inicio, DateOnly fim)
    {
        ValidarDatas(inicio, fim);
        return new PeriodoComissao(negocioId, inicio, fim);
    }

    public bool Contem(DateOnly dia) => dia >= Inicio && dia <= Fim;

    public void AlterarDatas(DateOnly inicio, DateOnly fim)
    {
        GarantirAberta("alterar as datas");
        ValidarDatas(inicio, fim);
        Inicio = inicio;
        Fim = fim;
    }

    public void Fechar(Guid? usuarioId, DateTimeOffset agora)
    {
        GarantirAberta("fechar");
        Estado = EstadoPeriodoComissao.Fechada;
        FechadoEm = agora;
        FechadoPorUsuarioId = usuarioId;
    }

    public void Reabrir()
    {
        if (Estado != EstadoPeriodoComissao.Fechada)
            throw new InvalidOperationException("Só uma quinzena fechada pode ser reaberta.");

        Estado = EstadoPeriodoComissao.Aberta;
        FechadoEm = null;
        FechadoPorUsuarioId = null;
    }

    /// <summary>Excluir o período só enquanto aberto (fechado tem valores de acerto que alguém já recebeu).</summary>
    public void GarantirQuePodeExcluir() => GarantirAberta("excluir");

    private void GarantirAberta(string acao)
    {
        if (Estado != EstadoPeriodoComissao.Aberta)
            throw new InvalidOperationException($"Não é possível {acao} uma quinzena fechada. Reabra a quinzena antes.");
    }

    private static void ValidarDatas(DateOnly inicio, DateOnly fim)
    {
        if (inicio > fim)
            throw new ArgumentException("O início não pode ser depois do fim.", nameof(inicio));
    }

    /// <summary>
    /// Sugestão do período seguinte (seção 7): do dia 1 ao 15 e do 16 ao último dia do mês. Sem
    /// período anterior, a quinzena que contém <paramref name="hoje"/>. Se o último período não
    /// termina num dia 15 ou no fim do mês, a sugestão fecha a quinzena em curso a partir do dia seguinte.
    /// </summary>
    public static (DateOnly Inicio, DateOnly Fim) SugerirProximo(DateOnly? fimDoUltimo, DateOnly hoje)
    {
        var inicio = fimDoUltimo?.AddDays(1) ?? (hoje.Day <= 15 ? new DateOnly(hoje.Year, hoje.Month, 1) : new DateOnly(hoje.Year, hoje.Month, 16));
        var fim = inicio.Day <= 15
            ? new DateOnly(inicio.Year, inicio.Month, 15)
            : new DateOnly(inicio.Year, inicio.Month, DateTime.DaysInMonth(inicio.Year, inicio.Month));
        return (inicio, fim);
    }
}
