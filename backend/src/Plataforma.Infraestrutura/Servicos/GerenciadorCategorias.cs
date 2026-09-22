using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Dominio.Servicos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Servicos;

public sealed class GerenciadorCategorias : IGerenciadorCategorias
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public GerenciadorCategorias(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<CategoriaResumo>> ListarAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Categorias
            .OrderBy(c => c.Nome)
            .Select(c => new CategoriaResumo(c.Id, c.Nome, c.Ativa))
            .ToListAsync(cancellationToken);

    public async Task<Guid> CriarAsync(string nome, CancellationToken cancellationToken = default)
    {
        var categoria = Categoria.Criar(_contextoNegocio.NegocioId!.Value, nome);
        _dbContext.Categorias.Add(categoria);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return categoria.Id;
    }

    public async Task<bool> RenomearAsync(Guid categoriaId, string nome, CancellationToken cancellationToken = default)
    {
        var categoria = await _dbContext.Categorias.FindAsync([categoriaId], cancellationToken);
        if (categoria is null)
            return false;

        categoria.Renomear(nome);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DesativarAsync(Guid categoriaId, CancellationToken cancellationToken = default) =>
        await AlterarAtivaAsync(categoriaId, ativa: false, cancellationToken);

    public async Task<bool> AtivarAsync(Guid categoriaId, CancellationToken cancellationToken = default) =>
        await AlterarAtivaAsync(categoriaId, ativa: true, cancellationToken);

    private async Task<bool> AlterarAtivaAsync(Guid categoriaId, bool ativa, CancellationToken cancellationToken)
    {
        var categoria = await _dbContext.Categorias.FindAsync([categoriaId], cancellationToken);
        if (categoria is null)
            return false;

        if (ativa)
            categoria.Ativar();
        else
            categoria.Desativar();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
