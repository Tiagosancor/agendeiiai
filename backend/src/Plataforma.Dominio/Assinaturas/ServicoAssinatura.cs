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
}
