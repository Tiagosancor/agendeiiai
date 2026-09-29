using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Administracao;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Assinaturas;

public sealed class ServicoAssinaturaNegocio : IServicoAssinaturaNegocio
{
    private const int DiasDeAvisoNoPainel = 7;

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IConsultaSituacaoAssinatura _situacao;
    private readonly IEnumerable<IGatewayPagamento> _gateways;
    private readonly IGatewayPagamento _gatewayConfigurado;

    public ServicoAssinaturaNegocio(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IConsultaSituacaoAssinatura situacao,
        IEnumerable<IGatewayPagamento> gateways, IOptions<OpcoesCobranca> opcoes)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _situacao = situacao;
        _gateways = gateways;
        // O de Cobranca__Provedor cobra as contratações novas; quem já está vinculado a um gateway continua com ele.
        var nome = opcoes.Value.Provedor == ProvedorCobranca.Asaas ? GatewayPagamentoAsaas.Nome : "manual";
        _gatewayConfigurado = gateways.FirstOrDefault(g => g.Provedor == nome) ?? gateways.First();
    }

    /// <summary>O gateway em que a assinatura está contratada; sem vínculo, o configurado.</summary>
    private IGatewayPagamento GatewayDa(Assinatura assinatura) =>
        assinatura.ProvedorGateway is { } provedor && _gateways.FirstOrDefault(g => g.Provedor == provedor) is { } vinculado
            ? vinculado
            : _gatewayConfigurado;

    /// <summary>Contratado (com ID) no gateway automático em uso.</summary>
    private static bool ContratadaNoGateway(Assinatura assinatura, IGatewayPagamento gateway) =>
        gateway.AceitaWebhook && assinatura.ProvedorGateway == gateway.Provedor && assinatura.IdExternoGateway is not null;

    public async Task<DetalheAssinaturaNegocio?> ObterAsync(CancellationToken cancellationToken = default)
    {
        var assinatura = await CarregarAtualizadaAsync(cancellationToken);
        if (assinatura is null)
            return null;

        var plano = await _dbContext.Planos.AsNoTracking().FirstAsync(p => p.Id == assinatura.PlanoId, cancellationToken);
        var cobrancas = await _dbContext.CobrancasAssinatura.AsNoTracking()
            .OrderByDescending(c => c.PagoEm)
            .Select(c => new CobrancaAssinaturaDto(c.PagoEm, c.Valor, c.Forma, c.PeriodoInicio, c.PeriodoFim, c.Origem, c.EstornadaEm))
            .ToListAsync(cancellationToken);
        var ativos = await _dbContext.Profissionais.CountAsync(p => p.Ativo, cancellationToken);

        var gateway = GatewayDa(assinatura);
        return new DetalheAssinaturaNegocio(
            plano.Id, plano.Nome, assinatura.Periodicidade, assinatura.Estado, assinatura.PrecoMensalTravado, assinatura.ValorDoPeriodo,
            assinatura.FimTeste, assinatura.ProximoVencimento, assinatura.CarenciaAte, ativos, plano.MaximoProfissionais, cobrancas,
            gateway.AceitaWebhook, ContratadaNoGateway(assinatura, gateway), assinatura.DocumentoTitularMascarado,
            assinatura.CancelamentoAgendado ? assinatura.PrazoAtual : null);
    }

    public async Task<AvisoAssinatura?> ObterAvisoAsync(CancellationToken cancellationToken = default)
    {
        var assinatura = await CarregarAtualizadaAsync(cancellationToken);
        if (assinatura is null)
            return null;

        if (assinatura.Estado is EstadoAssinatura.Atrasada or EstadoAssinatura.Suspensa)
            return new AvisoAssinatura(assinatura.Estado, assinatura.CarenciaAte, null, Destacado: true);

        // Negócio migrado (ativo sem vencimento) nunca vê aviso.
        if (assinatura.PrazoAtual is not DateTimeOffset prazo)
            return null;

        var dias = (int)Math.Ceiling((prazo - DateTimeOffset.UtcNow).TotalDays);
        return dias <= DiasDeAvisoNoPainel ? new AvisoAssinatura(assinatura.Estado, prazo, dias, Destacado: false) : null;
    }

    public async Task<InstrucoesPagamento> ObterInstrucoesPagamentoAsync(CancellationToken cancellationToken = default)
    {
        var assinatura = await CarregarAtualizadaAsync(cancellationToken)
            ?? throw new RegraAssinaturaException("Este negócio não tem assinatura.");
        var plano = await _dbContext.Planos.AsNoTracking().FirstAsync(p => p.Id == assinatura.PlanoId, cancellationToken);

        return await GatewayDa(assinatura).GerarLinkPagamentoAsync(new DadosCobrancaGateway(
            assinatura.NegocioId, plano.Nome, assinatura.Periodicidade.ToString(), assinatura.ValorDoPeriodo,
            assinatura.IdExternoGateway), cancellationToken);
    }

    public async Task TrocarPlanoAsync(string autor, Guid planoId, Periodicidade periodicidade, CancellationToken cancellationToken = default)
    {
        var assinatura = await _dbContext.Assinaturas.Include(a => a.Historico).FirstOrDefaultAsync(cancellationToken)
            ?? throw new RegraAssinaturaException("Este negócio não tem assinatura.");
        var plano = await _dbContext.Planos.FirstOrDefaultAsync(p => p.Id == planoId, cancellationToken)
            ?? throw new RegraAssinaturaException("Plano não encontrado.");
        var ativos = await _dbContext.Profissionais.CountAsync(p => p.Ativo, cancellationToken);

        ServicoAssinatura.TrocarPlano(assinatura, plano, periodicidade, ativos, autor, DateTimeOffset.UtcNow);

        // Contratada no gateway: altera lá também, a partir da próxima cobrança. Sem contrato ainda, só aqui.
        var gateway = GatewayDa(assinatura);
        if (ContratadaNoGateway(assinatura, gateway) || !gateway.AceitaWebhook)
            await gateway.CriarOuAlterarAssinaturaAsync(new DadosAssinaturaGateway(
                assinatura.NegocioId, assinatura.IdClienteGateway, plano.Nome, periodicidade.ToString(), assinatura.ValorDoPeriodo,
                assinatura.IdExternoGateway, AssinaturaId: assinatura.Id), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<InstrucoesPagamento> ContratarAsync(
        string autor, string emailTitular, Guid planoId, Periodicidade periodicidade, string cpfCnpj,
        CancellationToken cancellationToken = default)
    {
        var documento = DocumentoTitular.Criar(cpfCnpj);
        var gateway = _gatewayConfigurado;
        if (!gateway.AceitaWebhook)
            throw new RegraAssinaturaException("O pagamento da assinatura é combinado pelo WhatsApp. Use \"Assinar agora\".");

        var assinatura = await _dbContext.Assinaturas.Include(a => a.Historico).FirstOrDefaultAsync(cancellationToken)
            ?? throw new RegraAssinaturaException("Este negócio não tem assinatura.");
        if (ContratadaNoGateway(assinatura, gateway) && !assinatura.CancelamentoAgendado)
            throw new RegraAssinaturaException("A assinatura já está contratada. Para mudar, use \"Trocar plano\".");

        var plano = await _dbContext.Planos.FirstOrDefaultAsync(p => p.Id == planoId, cancellationToken)
            ?? throw new RegraAssinaturaException("Plano não encontrado.");
        var ativos = await _dbContext.Profissionais.CountAsync(p => p.Ativo, cancellationToken);
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == assinatura.NegocioId, cancellationToken);
        var agora = DateTimeOffset.UtcNow;

        ServicoAssinatura.TrocarPlano(assinatura, plano, periodicidade, ativos, autor, agora);

        // Pagador: contratar de novo com o mesmo documento reaproveita o cadastro; documento diferente cria outro.
        var idCliente = assinatura.ProvedorGateway == gateway.Provedor && assinatura.IdClienteGateway is { } existente
            && assinatura.DocumentoTitularMascarado == documento.Mascarado()
            ? existente
            : await gateway.CriarClienteAsync(
                new DadosClienteGateway(assinatura.NegocioId, negocio.NomeExibido, emailTitular, null, documento.Digitos), cancellationToken)
              ?? throw new FalhaGatewayPagamentoException("O serviço de pagamento não devolveu o cadastro do pagador.");
        ServicoAssinatura.VincularClienteGateway(assinatura, idCliente, documento);

        // Assinatura cancelada no gateway (cancelamento agendado) não volta: contrata uma nova.
        var idAssinatura = await gateway.CriarOuAlterarAssinaturaAsync(new DadosAssinaturaGateway(
            assinatura.NegocioId, idCliente, plano.Nome, periodicidade.ToString(), assinatura.ValorDoPeriodo,
            PrimeiroVencimento: ServicoAssinatura.PrimeiroVencimento(assinatura, agora), AssinaturaId: assinatura.Id), cancellationToken)
            ?? throw new FalhaGatewayPagamentoException("O serviço de pagamento não devolveu a assinatura criada.");
        ServicoAssinatura.RegistrarContratacao(assinatura, gateway.Provedor, idAssinatura, autor, agora);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await gateway.GerarLinkPagamentoAsync(new DadosCobrancaGateway(
            assinatura.NegocioId, plano.Nome, periodicidade.ToString(), assinatura.ValorDoPeriodo, idAssinatura), cancellationToken);
    }

    public async Task<bool> CancelarAsync(string autor, CancellationToken cancellationToken = default)
    {
        var assinatura = await _dbContext.Assinaturas.Include(a => a.Historico).FirstOrDefaultAsync(cancellationToken)
            ?? throw new RegraAssinaturaException("Este negócio não tem assinatura.");

        var imediato = ServicoAssinatura.PedirCancelamento(assinatura, autor, DateTimeOffset.UtcNow);

        // Nenhuma cobrança nova daqui em diante; o que já foi pago continua valendo até o fim do período.
        var gateway = GatewayDa(assinatura);
        if (ContratadaNoGateway(assinatura, gateway))
            await gateway.CancelarAssinaturaAsync(assinatura.IdExternoGateway, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return imediato;
    }

    /// <summary>Aplica o tempo antes (mesma regra da API), depois lê o estado já gravado.</summary>
    private async Task<Assinatura?> CarregarAtualizadaAsync(CancellationToken cancellationToken)
    {
        await _situacao.ObterAsync(_contextoNegocio.NegocioId!.Value, cancellationToken);
        return await _dbContext.Assinaturas.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
    }
}
