using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Administracao;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Assinaturas;

public sealed class ServicoAssinaturaNegocio : IServicoAssinaturaNegocio
{
    private const int DiasDeAvisoNoPainel = 7;

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IConsultaSituacaoAssinatura _situacao;
    private readonly IGatewayPagamento _gateway;

    public ServicoAssinaturaNegocio(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IConsultaSituacaoAssinatura situacao,
        IEnumerable<IGatewayPagamento> gateways)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _situacao = situacao;
        // Enquanto houver um só gateway, é ele; com um real, o que estiver vinculado à assinatura.
        _gateway = gateways.First();
    }

    public async Task<DetalheAssinaturaNegocio?> ObterAsync(CancellationToken cancellationToken = default)
    {
        var assinatura = await CarregarAtualizadaAsync(cancellationToken);
        if (assinatura is null)
            return null;

        var plano = await _dbContext.Planos.AsNoTracking().FirstAsync(p => p.Id == assinatura.PlanoId, cancellationToken);
        var cobrancas = await _dbContext.CobrancasAssinatura.AsNoTracking()
            .OrderByDescending(c => c.PagoEm)
            .Select(c => new CobrancaAssinaturaDto(c.PagoEm, c.Valor, c.Forma, c.PeriodoInicio, c.PeriodoFim, c.Origem))
            .ToListAsync(cancellationToken);
        var ativos = await _dbContext.Profissionais.CountAsync(p => p.Ativo, cancellationToken);

        return new DetalheAssinaturaNegocio(
            plano.Id, plano.Nome, assinatura.Periodicidade, assinatura.Estado, assinatura.PrecoMensalTravado, assinatura.ValorDoPeriodo,
            assinatura.FimTeste, assinatura.ProximoVencimento, assinatura.CarenciaAte, ativos, plano.MaximoProfissionais, cobrancas);
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

        return await _gateway.GerarLinkPagamentoAsync(new DadosCobrancaGateway(
            assinatura.NegocioId, plano.Nome, assinatura.Periodicidade.ToString(), assinatura.ValorDoPeriodo), cancellationToken);
    }

    public async Task TrocarPlanoAsync(string autor, Guid planoId, Periodicidade periodicidade, CancellationToken cancellationToken = default)
    {
        var assinatura = await _dbContext.Assinaturas.Include(a => a.Historico).FirstOrDefaultAsync(cancellationToken)
            ?? throw new RegraAssinaturaException("Este negócio não tem assinatura.");
        var plano = await _dbContext.Planos.FirstOrDefaultAsync(p => p.Id == planoId, cancellationToken)
            ?? throw new RegraAssinaturaException("Plano não encontrado.");
        var ativos = await _dbContext.Profissionais.CountAsync(p => p.Ativo, cancellationToken);

        ServicoAssinatura.TrocarPlano(assinatura, plano, periodicidade, ativos, autor, DateTimeOffset.UtcNow);
        await _gateway.CriarOuAlterarAssinaturaAsync(new DadosAssinaturaGateway(
            assinatura.NegocioId, null, plano.Nome, periodicidade.ToString(), assinatura.ValorDoPeriodo), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Aplica o tempo antes (mesma regra da API), depois lê o estado já gravado.</summary>
    private async Task<Assinatura?> CarregarAtualizadaAsync(CancellationToken cancellationToken)
    {
        await _situacao.ObterAsync(_contextoNegocio.NegocioId!.Value, cancellationToken);
        return await _dbContext.Assinaturas.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
    }
}
