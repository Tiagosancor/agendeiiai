using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Negocios;

public sealed class ServicoPrimeirosPassos : IServicoPrimeirosPassos
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly OpcoesMarca _opcoesMarca;

    public ServicoPrimeirosPassos(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IOptions<OpcoesMarca> opcoesMarca)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _opcoesMarca = opcoesMarca.Value;
    }

    public async Task<PrimeirosPassos> ObterAsync(CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);

        return new PrimeirosPassos(
            await _dbContext.Servicos.AnyAsync(s => s.Ativo, cancellationToken),
            await _dbContext.Profissionais.AnyAsync(p => p.Ativo, cancellationToken),
            await _dbContext.HorariosTrabalho.AnyAsync(cancellationToken),
            negocio.LinkAgendamentoCopiado,
            negocio.ChecklistDispensado,
            ConstrutorUrlPublica.Construir(_opcoesMarca, negocio.Slug.Valor, "/"));
    }

    public async Task MarcarLinkCopiadoAsync(CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.FirstAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);
        negocio.MarcarLinkAgendamentoCopiado();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DispensarAsync(CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.FirstAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);
        negocio.DispensarChecklist();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
