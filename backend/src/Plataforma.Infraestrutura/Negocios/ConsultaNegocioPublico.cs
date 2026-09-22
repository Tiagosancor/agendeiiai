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

        return negocio is null ? null : Mapear(negocio);
    }

    private static NegocioResumo Mapear(Negocio negocio) => new(
        negocio.Id, negocio.Slug.Valor, negocio.NomeExibido, negocio.Tipo.ToString(), negocio.Fuso,
        negocio.LogoUrl, negocio.CorPrimaria, negocio.CorSecundaria,
        negocio.TituloPagina, negocio.SubtituloPagina, negocio.TextoSobre,
        negocio.Endereco.Bairro, negocio.Endereco.Cidade, negocio.Endereco.Rua, negocio.Endereco.Numero, negocio.Endereco.Cep,
        negocio.Telefone, negocio.RedesSociais.Instagram, negocio.RedesSociais.Facebook, negocio.RedesSociais.WhatsApp,
        negocio.HorarioFuncionamento
            .Select(h => new HorarioFuncionamentoDiaDto((int)h.DiaSemana, h.Abertura, h.Fechamento, h.Fechado))
            .ToList());
}
