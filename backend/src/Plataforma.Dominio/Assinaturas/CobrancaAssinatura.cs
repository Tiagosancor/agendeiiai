using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Assinaturas;

public enum FormaCobranca
{
    Pix,
    Boleto,
    Cartao,
    Transferencia,
    Outro,
}

public enum OrigemCobranca
{
    Manual,
    Gateway,
}

/// <summary>
/// O que o NEGÓCIO paga ao produto pela assinatura. Não confundir com <c>Pagamento</c>, que é
/// o pagamento de um atendimento pelo cliente final do negócio (seção 10).
/// </summary>
public class CobrancaAssinatura : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid AssinaturaId { get; private set; }

    public decimal Valor { get; private set; }

    public FormaCobranca Forma { get; private set; }

    public DateTimeOffset PagoEm { get; private set; }

    public DateTimeOffset PeriodoInicio { get; private set; }

    public DateTimeOffset PeriodoFim { get; private set; }

    public OrigemCobranca Origem { get; private set; }

    /// <summary>ID da cobrança no gateway (nulo no pagamento manual).</summary>
    public string? IdExterno { get; private set; }

    public string Autor { get; private set; } = string.Empty;

    protected CobrancaAssinatura()
    {
    }

    internal CobrancaAssinatura(
        Guid negocioId, Guid assinaturaId, decimal valor, FormaCobranca forma, DateTimeOffset pagoEm,
        DateTimeOffset periodoInicio, DateTimeOffset periodoFim, OrigemCobranca origem, string? idExterno, string autor)
    {
        NegocioId = negocioId;
        AssinaturaId = assinaturaId;
        Valor = valor;
        Forma = forma;
        PagoEm = pagoEm;
        PeriodoInicio = periodoInicio;
        PeriodoFim = periodoFim;
        Origem = origem;
        IdExterno = idExterno;
        Autor = autor;
    }
}
