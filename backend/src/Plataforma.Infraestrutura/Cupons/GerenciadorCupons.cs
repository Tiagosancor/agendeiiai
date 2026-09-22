using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Cupons;
using Plataforma.Dominio.Cupons;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Cupons;

public sealed class GerenciadorCupons : IGerenciadorCupons
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public GerenciadorCupons(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<CupomResumo>> ListarAsync(CancellationToken cancellationToken = default)
    {
        var cupons = await _dbContext.Cupons.AsNoTracking().OrderBy(c => c.Codigo).ToListAsync(cancellationToken);
        return cupons.Select(Mapear).ToList();
    }

    public async Task<Guid> CriarAsync(CriarCupom dados, CancellationToken cancellationToken = default)
    {
        var cupom = Cupom.Criar(
            _contextoNegocio.NegocioId!.Value, dados.Codigo, Enum.Parse<TipoDescontoCupom>(dados.Tipo), dados.Valor,
            dados.ValidoAte, dados.LimiteUsos, dados.ServicoIdsEscopo);

        _dbContext.Cupons.Add(cupom);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return cupom.Id;
    }

    public async Task<bool> AtualizarAsync(Guid cupomId, AtualizarCupom dados, CancellationToken cancellationToken = default)
    {
        var cupom = await _dbContext.Cupons.FindAsync([cupomId], cancellationToken);
        if (cupom is null)
            return false;

        cupom.AtualizarCondicoes(dados.ValidoAte, dados.LimiteUsos, dados.ServicoIdsEscopo);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> DesativarAsync(Guid cupomId, CancellationToken cancellationToken = default) =>
        AlterarAtivoAsync(cupomId, ativo: false, cancellationToken);

    public Task<bool> AtivarAsync(Guid cupomId, CancellationToken cancellationToken = default) =>
        AlterarAtivoAsync(cupomId, ativo: true, cancellationToken);

    private async Task<bool> AlterarAtivoAsync(Guid cupomId, bool ativo, CancellationToken cancellationToken)
    {
        var cupom = await _dbContext.Cupons.FindAsync([cupomId], cancellationToken);
        if (cupom is null)
            return false;

        if (ativo)
            cupom.Ativar();
        else
            cupom.Desativar();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static CupomResumo Mapear(Cupom cupom) => new(
        cupom.Id, cupom.Codigo, cupom.Tipo.ToString(), cupom.Valor, cupom.ValidoAte,
        cupom.LimiteUsos, cupom.UsosAtuais, cupom.Ativo, cupom.ServicoIdsEscopo);
}
