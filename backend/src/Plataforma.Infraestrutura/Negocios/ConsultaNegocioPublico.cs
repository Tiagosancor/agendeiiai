using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Negocios;

public sealed class ConsultaNegocioPublico : IConsultaNegocioPublico
{
    private readonly PlataformaDbContext _dbContext;

    public ConsultaNegocioPublico(PlataformaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<NegocioResumo?> ObterPorSlugAsync(Slug slug, CancellationToken cancellationToken = default)
    {
        // Comparação pelo objeto Slug inteiro (não por .Valor): o value converter traduz os
        // dois lados para a coluna de texto. Acessar .Valor dentro da expressão não seria
        // traduzível pelo EF, por isso a projeção final acontece em memória, após buscar a linha.
        var negocio = await _dbContext.Negocios
            .AsNoTracking()
            .Where(n => n.Ativo && n.Slug == slug)
            .FirstOrDefaultAsync(cancellationToken);

        return negocio is null
            ? null
            : new NegocioResumo(negocio.Id, negocio.Slug.Valor, negocio.NomeExibido, negocio.Tipo.ToString(), negocio.Fuso);
    }
}
