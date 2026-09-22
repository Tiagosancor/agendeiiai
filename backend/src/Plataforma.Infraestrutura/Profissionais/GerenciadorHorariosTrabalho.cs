using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Profissionais;

public sealed class GerenciadorHorariosTrabalho : IGerenciadorHorariosTrabalho
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public GerenciadorHorariosTrabalho(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<IntervaloTrabalho>> ListarAsync(Guid profissionalId, CancellationToken cancellationToken = default) =>
        await _dbContext.HorariosTrabalho.AsNoTracking()
            .Where(h => h.ProfissionalId == profissionalId)
            .OrderBy(h => h.DiaSemana).ThenBy(h => h.Inicio)
            .Select(h => new IntervaloTrabalho((int)h.DiaSemana, h.Inicio, h.Fim))
            .ToListAsync(cancellationToken);

    public async Task DefinirAsync(
        Guid profissionalId, IReadOnlyList<IntervaloTrabalho> intervalos, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;

        var existentes = await _dbContext.HorariosTrabalho
            .Where(h => h.ProfissionalId == profissionalId)
            .ToListAsync(cancellationToken);

        _dbContext.HorariosTrabalho.RemoveRange(existentes);

        foreach (var intervalo in intervalos)
        {
            _dbContext.HorariosTrabalho.Add(HorarioTrabalho.Criar(
                negocioId, profissionalId, (DiaSemana)intervalo.DiaSemana, intervalo.Inicio, intervalo.Fim));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
