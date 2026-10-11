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

    public Task<bool> AtualizarAsync(Guid cupomId, AtualizarCupom dados, CancellationToken cancellationToken = default) =>
        AlterarComLinhaTravadaAsync(cupomId, c => c.AtualizarCondicoes(dados.ValidoAte, dados.LimiteUsos, dados.ServicoIdsEscopo), cancellationToken);

    public Task<bool> DesativarAsync(Guid cupomId, CancellationToken cancellationToken = default) =>
        AlterarComLinhaTravadaAsync(cupomId, c => c.Desativar(), cancellationToken);

    public Task<bool> AtivarAsync(Guid cupomId, CancellationToken cancellationToken = default) =>
        AlterarComLinhaTravadaAsync(cupomId, c => c.Ativar(), cancellationToken);

    /// <summary>
    /// Mesma trava de linha da confirmação pública (<see cref="TravaCupom"/>): editar, desativar ou reduzir o limite
    /// espera a confirmação em andamento terminar, e a confirmação seguinte já vê o cupom alterado.
    /// </summary>
    private Task<bool> AlterarComLinhaTravadaAsync(Guid cupomId, Action<Cupom> alteracao, CancellationToken cancellationToken)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        var tentativas = new TentativasDaTransacao(_dbContext);

        return estrategia.ExecuteAsync(async () =>
        {
            tentativas.Iniciar();
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var cupom = await TravaCupom.TravarPorIdAsync(_dbContext, negocioId, cupomId, cancellationToken);
            if (cupom is null)
                return false;

            alteracao(cupom);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return true;
        });
    }

    private static CupomResumo Mapear(Cupom cupom) => new(
        cupom.Id, cupom.Codigo, cupom.Tipo.ToString(), cupom.Valor, cupom.ValidoAte,
        cupom.LimiteUsos, cupom.UsosAtuais, cupom.Ativo, cupom.ServicoIdsEscopo);
}
