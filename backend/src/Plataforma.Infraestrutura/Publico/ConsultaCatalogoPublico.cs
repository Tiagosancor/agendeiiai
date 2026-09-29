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

        var executados = ServicosExecutadosPorProfissionalAtivo();
        var servicos = await _dbContext.Servicos.AsNoTracking()
            .Where(s => s.Ativo && executados.Contains(s.Id))
            .OrderByDescending(s => s.Popular).ThenBy(s => s.Nome)
            .ToListAsync(cancellationToken);

        return categorias
            .Select(categoria => new CategoriaComServicosPublicos(
                categoria.Id, categoria.Nome,
                servicos.Where(s => s.CategoriaId == categoria.Id)
                    .Select(s => new ServicoPublico(s.Id, s.Nome, s.Preco, s.DuracaoMinutos, s.Popular, s.ExibirNaPaginaInicial))
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

        // Só serviços ativos: um serviço desativado não pode ser oferecido por ninguém.
        var vinculos = await (
            from v in _dbContext.ProfissionalServicos.AsNoTracking()
            join s in _dbContext.Servicos on v.ServicoId equals s.Id
            where s.Ativo
            select new
            {
                v.ProfissionalId,
                v.ServicoId,
                Preco = v.PrecoPersonalizado ?? s.Preco,
                Duracao = v.DuracaoPersonalizadaMinutos ?? s.DuracaoMinutos,
            }).ToListAsync(cancellationToken);

        return profissionais
            .Select(p =>
            {
                var dele = vinculos.Where(v => v.ProfissionalId == p.Id).ToList();
                return new ProfissionalPublico(
                    p.Id, p.Nome, p.FotoUrl, p.Funcao,
                    dele.Select(v => v.ServicoId).ToList(),
                    dele.Select(v => new ServicoDoProfissionalPublico(v.ServicoId, v.Preco, v.Duracao)).ToList());
            })
            .ToList();
    }

    private IQueryable<Guid> ServicosExecutadosPorProfissionalAtivo() =>
        from v in _dbContext.ProfissionalServicos
        join p in _dbContext.Profissionais on v.ProfissionalId equals p.Id
        where p.Ativo
        select v.ServicoId;
}
