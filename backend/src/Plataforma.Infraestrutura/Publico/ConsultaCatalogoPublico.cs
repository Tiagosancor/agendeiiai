using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Publico;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Publico;

public sealed class ConsultaCatalogoPublico : IConsultaCatalogoPublico
{
    private readonly PlataformaDbContext _dbContext;

    public ConsultaCatalogoPublico(PlataformaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CategoriaComServicosPublicos>> ListarServicosAsync(CancellationToken cancellationToken = default)
    {
        var categorias = await _dbContext.Categorias.AsNoTracking()
            .Where(c => c.Ativa)
            .OrderBy(c => c.Nome)
            .ToListAsync(cancellationToken);

        var servicos = await _dbContext.Servicos.AsNoTracking()
            .Where(s => s.Ativo)
            .OrderByDescending(s => s.Popular).ThenBy(s => s.Nome)
            .ToListAsync(cancellationToken);

        return categorias
            .Select(categoria => new CategoriaComServicosPublicos(
                categoria.Id, categoria.Nome,
                servicos.Where(s => s.CategoriaId == categoria.Id)
                    .Select(s => new ServicoPublico(s.Id, s.Nome, s.Preco, s.DuracaoMinutos, s.Popular))
                    .ToList()))
            .Where(c => c.Servicos.Count > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<ProfissionalPublico>> ListarProfissionaisAsync(CancellationToken cancellationToken = default)
    {
        var profissionais = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Ativo)
            .OrderBy(p => p.Nome)
            .ToListAsync(cancellationToken);

        var vinculos = await _dbContext.ProfissionalServicos.AsNoTracking().ToListAsync(cancellationToken);

        return profissionais
            .Select(p => new ProfissionalPublico(
                p.Id, p.Nome, p.FotoUrl, p.Funcao,
                vinculos.Where(v => v.ProfissionalId == p.Id).Select(v => v.ServicoId).ToList()))
            .ToList();
    }
}
