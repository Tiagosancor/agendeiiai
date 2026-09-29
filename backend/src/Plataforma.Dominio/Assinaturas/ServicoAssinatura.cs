namespace Plataforma.Dominio.Assinaturas;

/// <summary>
/// Único lugar que muda o estado de uma <see cref="Assinatura"/> (seção 7). Toda mudança de
/// estado fica no histórico. Puro (sem banco): quem chama carrega e grava, e passa o "agora".
/// </summary>
public static class ServicoAssinatura
{
    public const string AutorSistema = "sistema";

    public static Assinatura IniciarTeste(Guid negocioId, Plano plano, Periodicidade periodicidade, string autor, DateTimeOffset agora)
    {
        if (!plano.Ativo)
            throw new RegraAssinaturaException("Este plano não está disponível.");

        var assinatura = new Assinatura(negocioId, plano, periodicidade);
        assinatura.DefinirFimTeste(agora.AddDays(Assinatura.DiasTeste));
        assinatura.RegistrarCriacao(EstadoAssinatura.EmTeste, autor, $"Teste grátis de {Assinatura.DiasTeste} dias", agora);
        return assinatura;
    }

    /// <summary>
    /// Aplica o que o tempo decide: teste ou vencimento passado → carência; carência passada →
    /// suspensa. Encadeia (um teste que acabou há 10 dias vai direto a suspensa, com os dois
    /// passos no histórico). Devolve se mudou algo.
    /// </summary>
    public static bool AtualizarPorTempo(Assinatura assinatura, DateTimeOffset agora)
    {
        var mudou = false;

        while (true)
        {
            if (assinatura.Estado is EstadoAssinatura.EmTeste or EstadoAssinatura.Ativa
                && assinatura.PrazoAtual is DateTimeOffset prazoCancelamento && agora >= prazoCancelamento
                && assinatura.CancelamentoAgendado)
            {
                assinatura.MudarEstado(EstadoAssinatura.Cancelada, AutorSistema, "Fim do período — cancelamento pedido pelo negócio", agora);
                return true;
            }

            if (assinatura.Estado is EstadoAssinatura.EmTeste or EstadoAssinatura.Ativa
                && assinatura.PrazoAtual is DateTimeOffset prazo && agora >= prazo)
            {
                var motivo = assinatura.Estado == EstadoAssinatura.EmTeste
                    ? "Fim do teste sem pagamento"
                    : "Vencimento sem pagamento";
                assinatura.DefinirCarencia(prazo.AddDays(Assinatura.DiasCarencia));
                assinatura.MudarEstado(EstadoAssinatura.Atrasada, AutorSistema, motivo, agora);
                mudou = true;
                continue;
            }

            if (assinatura.Estado == EstadoAssinatura.Atrasada
                && assinatura.CarenciaAte is DateTimeOffset carencia && agora >= carencia)
            {
                assinatura.MudarEstado(EstadoAssinatura.Suspensa, AutorSistema, "Fim da carência sem pagamento", agora);
                mudou = true;
                continue;
            }

            return mudou;
        }
    }

    /// <summary>Pagamento de um período (manual ou pelo gateway): volta a <c>Ativa</c> na hora e define o próximo vencimento.</summary>
    public static CobrancaAssinatura RegistrarPagamento(
        Assinatura assinatura, decimal valor, FormaCobranca forma, DateTimeOffset pagoEm,
        DateTimeOffset periodoInicio, DateTimeOffset periodoFim, OrigemCobranca origem, string? idExterno,
        string autor, DateTimeOffset agora)
    {
        if (valor <= 0)
            throw new RegraAssinaturaException("O valor precisa ser maior que zero.");

        if (periodoFim <= periodoInicio)
            throw new RegraAssinaturaException("O fim do período precisa ser depois do início.");

        if (assinatura.Estado == EstadoAssinatura.Cancelada)
            throw new RegraAssinaturaException("A assinatura está cancelada.");

        var cobranca = new CobrancaAssinatura(
            assinatura.NegocioId, assinatura.Id, valor, forma, pagoEm, periodoInicio, periodoFim, origem, idExterno, autor);

        assinatura.DefinirVencimento(periodoFim);
        assinatura.DefinirCarencia(null);

        if (assinatura.Estado != EstadoAssinatura.Ativa)
            assinatura.MudarEstado(EstadoAssinatura.Ativa, autor, $"Pagamento registrado ({origem}) até {periodoFim:dd/MM/yyyy}", agora);
        else
            assinatura.RegistrarObservacao(autor, $"Pagamento registrado ({origem}) até {periodoFim:dd/MM/yyyy}", agora);

        return cobranca;
    }

    /// <summary>Só para quem nunca pagou (teste, ou carência/suspensão vindas do teste). Volta a <c>EmTeste</c>.</summary>
    public static void EstenderTeste(Assinatura assinatura, int dias, string autor, DateTimeOffset agora)
    {
        if (dias is < 1 or > 90)
            throw new RegraAssinaturaException("Estenda o teste entre 1 e 90 dias.");

        if (!assinatura.NuncaPagou || assinatura.Estado is EstadoAssinatura.Ativa or EstadoAssinatura.Cancelada)
            throw new RegraAssinaturaException("Só dá para estender o teste de quem ainda não pagou.");

        var aPartirDe = assinatura.FimTeste is DateTimeOffset fim && fim > agora ? fim : agora;
        assinatura.DefinirFimTeste(aPartirDe.AddDays(dias));
        assinatura.DefinirCarencia(null);

        var motivo = $"Teste estendido em {dias} dias (até {assinatura.FimTeste:dd/MM/yyyy})";
        if (assinatura.Estado != EstadoAssinatura.EmTeste)
            assinatura.MudarEstado(EstadoAssinatura.EmTeste, autor, motivo, agora);
        else
            assinatura.RegistrarObservacao(autor, motivo, agora);
    }

    /// <summary>Recusa ir para um plano que não comporta os profissionais ativos de hoje (seção 7).</summary>
    public static void TrocarPlano(
        Assinatura assinatura, Plano novoPlano, Periodicidade periodicidade, int profissionaisAtivos,
        string autor, DateTimeOffset agora)
    {
        if (!novoPlano.Ativo)
            throw new RegraAssinaturaException("Este plano não está disponível.");

        if (assinatura.Estado == EstadoAssinatura.Cancelada)
            throw new RegraAssinaturaException("A assinatura está cancelada.");

        if (!novoPlano.Comporta(profissionaisAtivos))
            throw new LimiteProfissionaisExcedidoException(novoPlano.MaximoProfissionais, profissionaisAtivos);

        assinatura.DefinirPlano(novoPlano, periodicidade);
        assinatura.RegistrarObservacao(autor, $"Plano alterado para {novoPlano.Nome} ({periodicidade})", agora);
    }

    public static void Suspender(Assinatura assinatura, string motivo, string autor, DateTimeOffset agora)
    {
        if (assinatura.Estado is EstadoAssinatura.Suspensa or EstadoAssinatura.Cancelada)
            throw new RegraAssinaturaException("A assinatura já está suspensa ou cancelada.");

        assinatura.MudarEstado(EstadoAssinatura.Suspensa, autor, motivo, agora);
    }

    /// <summary>
    /// Desfaz uma suspensão voltando ao estado de antes dela. Se era carência, ganha uma carência
    /// nova a partir de agora — senão o próprio job suspenderia de novo no minuto seguinte.
    /// </summary>
    public static void Reativar(Assinatura assinatura, string autor, DateTimeOffset agora)
    {
        if (assinatura.Estado != EstadoAssinatura.Suspensa)
            throw new RegraAssinaturaException("Só dá para reativar uma assinatura suspensa.");

        var anterior = assinatura.Historico
            .Where(h => h.EstadoNovo == EstadoAssinatura.Suspensa && h.EstadoAnterior is not null)
            .OrderBy(h => h.CriadoEm)
            .LastOrDefault()?.EstadoAnterior ?? EstadoAssinatura.Ativa;

        if (anterior == EstadoAssinatura.Atrasada)
            assinatura.DefinirCarencia(agora.AddDays(Assinatura.DiasCarencia));

        assinatura.MudarEstado(anterior, autor, "Reativada", agora);
    }

    public static void Cancelar(Assinatura assinatura, string motivo, string autor, DateTimeOffset agora)
    {
        if (assinatura.Estado == EstadoAssinatura.Cancelada)
            throw new RegraAssinaturaException("A assinatura já está cancelada.");

        assinatura.MudarEstado(EstadoAssinatura.Cancelada, autor, motivo, agora);
    }

    public static void VincularGateway(Assinatura assinatura, string provedor, string idExterno) =>
        assinatura.VincularGateway(provedor, idExterno);

    public static void VincularClienteGateway(Assinatura assinatura, string idCliente, DocumentoTitular documento) =>
        assinatura.VincularClienteGateway(idCliente, documento.Mascarado());

    /// <summary>
    /// Vencimento da primeira cobrança ao contratar pelo gateway: o fim do teste ou do período já pago, se ainda está no
    /// futuro (nunca cobra antes disso); senão hoje.
    /// </summary>
    public static DateTimeOffset PrimeiroVencimento(Assinatura assinatura, DateTimeOffset agora) =>
        assinatura.PrazoAtual is DateTimeOffset prazo && prazo > agora ? prazo : agora;

    /// <summary>Contratar (de novo) pelo gateway desfaz um cancelamento pedido e ainda não efetivado.</summary>
    public static void RegistrarContratacao(Assinatura assinatura, string provedor, string idExterno, string autor, DateTimeOffset agora)
    {
        if (assinatura.Estado == EstadoAssinatura.Cancelada)
            throw new RegraAssinaturaException("A assinatura está cancelada.");

        assinatura.VincularGateway(provedor, idExterno);
        assinatura.DefinirCancelamentoPedido(null);
        assinatura.RegistrarObservacao(autor, "Assinatura contratada no gateway de pagamento", agora);
    }

    /// <summary>
    /// Cancelamento pedido pelo negócio (decisão do dono): com período pago (ou teste) pela frente, usa até o fim dele e
    /// então vira <c>Cancelada</c>; sem nada pela frente (atrasada, suspensa, migrada sem vencimento), cancela na hora.
    /// Devolve se cancelou na hora.
    /// </summary>
    public static bool PedirCancelamento(Assinatura assinatura, string autor, DateTimeOffset agora)
    {
        if (assinatura.Estado == EstadoAssinatura.Cancelada || assinatura.CancelamentoAgendado)
            throw new RegraAssinaturaException("O cancelamento já foi pedido.");

        assinatura.DefinirCancelamentoPedido(agora);

        if (assinatura.Estado is EstadoAssinatura.EmTeste or EstadoAssinatura.Ativa
            && assinatura.PrazoAtual is DateTimeOffset prazo && prazo > agora)
        {
            assinatura.RegistrarObservacao(autor, $"Cancelamento pedido — vale até {prazo:dd/MM/yyyy}", agora);
            return false;
        }

        assinatura.MudarEstado(EstadoAssinatura.Cancelada, autor, "Cancelada a pedido do negócio", agora);
        return true;
    }

    /// <summary>
    /// O gateway avisou que a cobrança venceu sem pagamento: entra em carência contada do vencimento, mesmo que o relógio
    /// daqui ainda não tenha chegado lá. Fora de <c>EmTeste</c>/<c>Ativa</c>, nada muda.
    /// </summary>
    public static bool MarcarCobrancaVencida(Assinatura assinatura, DateTimeOffset vencimento, string autor, DateTimeOffset agora)
    {
        if (assinatura.Estado is not (EstadoAssinatura.EmTeste or EstadoAssinatura.Ativa))
            return false;

        if (assinatura.CancelamentoAgendado)
        {
            assinatura.MudarEstado(EstadoAssinatura.Cancelada, autor, "Cobrança vencida com cancelamento pedido", agora);
            return true;
        }

        assinatura.DefinirCarencia(vencimento.AddDays(Assinatura.DiasCarencia));
        assinatura.MudarEstado(EstadoAssinatura.Atrasada, autor, $"Cobrança vencida em {vencimento:dd/MM/yyyy}", agora);
        return true;
    }

    /// <summary>
    /// Pagamento estornado ou contestado (decisão do dono): o período dele deixa de valer e a assinatura volta a
    /// <c>Atrasada</c>, com carência contada a partir de agora. Já atrasada ou suspensa: só fica no histórico.
    /// </summary>
    public static void EstornarPagamento(Assinatura assinatura, CobrancaAssinatura cobranca, string autor, DateTimeOffset agora)
    {
        if (cobranca.EstornadaEm is not null)
            return;

        cobranca.MarcarEstornada(agora);
        if (assinatura.ProximoVencimento == cobranca.PeriodoFim)
            assinatura.DefinirVencimento(cobranca.PeriodoInicio);

        var motivo = $"Pagamento do período {cobranca.PeriodoInicio:dd/MM/yyyy} a {cobranca.PeriodoFim:dd/MM/yyyy} estornado no gateway";
        if (assinatura.Estado == EstadoAssinatura.Ativa)
        {
            assinatura.DefinirCarencia(agora.AddDays(Assinatura.DiasCarencia));
            assinatura.MudarEstado(EstadoAssinatura.Atrasada, autor, motivo, agora);
        }
        else
        {
            assinatura.RegistrarObservacao(autor, motivo, agora);
        }
    }
}
