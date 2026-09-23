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

    public async Task<IReadOnlyList<IntervaloTrabalho>> ListarAsync(Guid profissionalId, CancellationToken cancellationToken = default)
    {
        // DiaSemana é gravado como texto (HasConversion<string>() — HorarioTrabalhoConfiguracao),
        // então tanto o (int) quanto o OrderBy por DiaSemana precisam acontecer em memória: o EF
        // tenta traduzir (int)h.DiaSemana num CAST direto sobre a coluna de texto ("Segunda" não
        // é um integer válido no Postgres — 500 sempre que existisse pelo menos uma linha) e um
        // OrderBy(h => h.DiaSemana) traduzido pro SQL ordenaria pelo texto em português
        // (alfabético), não pela ordem real da semana.
        var horarios = await _dbContext.HorariosTrabalho.AsNoTracking()
            .Where(h => h.ProfissionalId == profissionalId)
            .ToListAsync(cancellationToken);

        return horarios
            .OrderBy(h => h.DiaSemana).ThenBy(h => h.Inicio)
            .Select(h => new IntervaloTrabalho((int)h.DiaSemana, h.Inicio, h.Fim))
            .ToList();
    }

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
