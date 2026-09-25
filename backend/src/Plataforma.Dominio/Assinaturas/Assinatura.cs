using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Assinaturas;

/// <summary>
/// Assinatura do negócio com o produto (seção 7) — uma por negócio. Tudo que muda estado,
/// prazo ou plano é <c>internal</c>: só o <see cref="ServicoAssinatura"/> (mesmo assembly)
/// altera, e é por ele que o gateway de pagamento vai entrar depois.
/// </summary>
public class Assinatura : EntidadeBase, IEntidadeDoNegocio
{
    public const int DiasTeste = 30;
    public const int DiasCarencia = 5;

    /// <summary>Avisos por e-mail antes do fim do teste ou do vencimento (seção 7).</summary>
    public static readonly IReadOnlyList<int> DiasDeAviso = [7, 3, 1];

    private readonly List<HistoricoAssinatura> _historico = [];

    public Guid NegocioId { get; private set; }

    public Guid PlanoId { get; private set; }

    public Periodicidade Periodicidade { get; private set; }

    /// <summary>Preço por mês vigente na contratação — mudar a tabela de planos nunca é retroativo.</summary>
    public decimal PrecoMensalTravado { get; private set; }

    public EstadoAssinatura Estado { get; private set; }

    public DateTimeOffset? FimTeste { get; private set; }

    /// <summary>Nulo enquanto nunca houve pagamento — e nos negócios migrados de antes das assinaturas (sem vencimento).</summary>
    public DateTimeOffset? ProximoVencimento { get; private set; }

    public DateTimeOffset? CarenciaAte { get; private set; }

    /// <summary>Menor aviso (7/3/1 dias) já enviado para o prazo atual — evita reenvio e zera quando o prazo muda.</summary>
    public int? UltimoAvisoDias { get; private set; }

    public string? ProvedorGateway { get; private set; }

    public string? IdExternoGateway { get; private set; }

    public IReadOnlyCollection<HistoricoAssinatura> Historico => _historico.AsReadOnly();

    protected Assinatura()
    {
    }

    internal Assinatura(Guid negocioId, Plano plano, Periodicidade periodicidade)
    {
        NegocioId = negocioId;
        PlanoId = plano.Id;
        Periodicidade = periodicidade;
        PrecoMensalTravado = plano.PrecoPorMes(periodicidade);
    }

    /// <summary>Valor de um período (mês ou ano) pelo preço travado.</summary>
    public decimal ValorDoPeriodo => Periodicidade == Periodicidade.Anual ? PrecoMensalTravado * 12 : PrecoMensalTravado;

    /// <summary>Data que o próximo aviso mira: fim do teste ou vencimento. Nulo nos demais estados.</summary>
    public DateTimeOffset? PrazoAtual => Estado switch
    {
        EstadoAssinatura.EmTeste => FimTeste,
        EstadoAssinatura.Ativa => ProximoVencimento,
        _ => null,
    };

    /// <summary>Nunca pagou: nasceu em teste e ainda não tem vencimento.</summary>
    public bool NuncaPagou => FimTeste is not null && ProximoVencimento is null;

    /// <summary>Negócio pode operar normalmente (painel e novos agendamentos públicos).</summary>
    public bool PermiteOperar => Estado is EstadoAssinatura.EmTeste or EstadoAssinatura.Ativa or EstadoAssinatura.Atrasada;

    /// <summary>
    /// O aviso (em dias) que deveria sair agora, ou nulo. Escolhe o mais urgente ainda não
    /// enviado — se o job ficou dias parado (hibernação), manda só o atual, não os três.
    /// </summary>
    public int? AvisoPendente(DateTimeOffset agora)
    {
        if (PrazoAtual is not DateTimeOffset prazo)
            return null;

        var diasRestantes = (prazo - agora).TotalDays;
        if (diasRestantes <= 0)
            return null;

        var aplicaveis = DiasDeAviso.Where(d => diasRestantes <= d && (UltimoAvisoDias is null || d < UltimoAvisoDias)).ToList();
        return aplicaveis.Count == 0 ? null : aplicaveis.Min();
    }

    public void MarcarAvisoEnviado(int dias) => UltimoAvisoDias = dias;

    internal void MudarEstado(EstadoAssinatura novo, string autor, string motivo, DateTimeOffset agora)
    {
        var anterior = Estado;
        Estado = novo;
        UltimoAvisoDias = null;
        _historico.Add(new HistoricoAssinatura(NegocioId, Id, anterior, novo, autor, motivo, agora));
    }

    internal void RegistrarCriacao(EstadoAssinatura estado, string autor, string motivo, DateTimeOffset agora)
    {
        Estado = estado;
        _historico.Add(new HistoricoAssinatura(NegocioId, Id, null, estado, autor, motivo, agora));
    }

    internal void RegistrarObservacao(string autor, string motivo, DateTimeOffset agora) =>
        _historico.Add(new HistoricoAssinatura(NegocioId, Id, Estado, Estado, autor, motivo, agora));

    internal void DefinirFimTeste(DateTimeOffset? fimTeste)
    {
        FimTeste = fimTeste;
        UltimoAvisoDias = null;
    }

    internal void DefinirVencimento(DateTimeOffset? vencimento)
    {
        ProximoVencimento = vencimento;
        UltimoAvisoDias = null;
    }

    internal void DefinirCarencia(DateTimeOffset? carenciaAte) => CarenciaAte = carenciaAte;

    internal void DefinirPlano(Plano plano, Periodicidade periodicidade)
    {
        PlanoId = plano.Id;
        Periodicidade = periodicidade;
        PrecoMensalTravado = plano.PrecoPorMes(periodicidade);
    }

    internal void VincularGateway(string provedor, string idExterno)
    {
        ProvedorGateway = provedor;
        IdExternoGateway = idExterno;
    }
}
