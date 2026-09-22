using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Profissionais;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Profissionais;

public sealed class GerenciadorBloqueios : IGerenciadorBloqueios
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public GerenciadorBloqueios(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<BloqueioResumo>> ListarAsync(Guid profissionalId, CancellationToken cancellationToken = default) =>
        await _dbContext.BloqueiosAgenda.AsNoTracking()
            .Where(b => b.ProfissionalId == profissionalId)
            .OrderBy(b => b.InicioUtc)
            .Select(b => new BloqueioResumo(b.Id, b.ProfissionalId, b.InicioUtc, b.FimUtc, b.Motivo))
            .ToListAsync(cancellationToken);

    public async Task<Guid> CriarAsync(
        Guid profissionalId, DateTimeOffset inicioUtc, DateTimeOffset fimUtc, string? motivo,
        CancellationToken cancellationToken = default)
    {
        var bloqueio = BloqueioAgenda.Criar(_contextoNegocio.NegocioId!.Value, profissionalId, inicioUtc, fimUtc, motivo);
        _dbContext.BloqueiosAgenda.Add(bloqueio);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return bloqueio.Id;
    }

    public async Task<bool> RemoverAsync(Guid bloqueioId, CancellationToken cancellationToken = default)
    {
        var bloqueio = await _dbContext.BloqueiosAgenda.FindAsync([bloqueioId], cancellationToken);
        if (bloqueio is null)
            return false;

        _dbContext.BloqueiosAgenda.Remove(bloqueio);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
