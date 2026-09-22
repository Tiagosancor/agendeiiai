using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Dominio.Servicos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Servicos;

public sealed class GerenciadorProfissionalServicos : IGerenciadorProfissionalServicos
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public GerenciadorProfissionalServicos(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<ProfissionalServicoResumo>> ListarAsync(
        Guid profissionalId, CancellationToken cancellationToken = default)
    {
        var vinculos = await _dbContext.ProfissionalServicos.AsNoTracking()
            .Where(ps => ps.ProfissionalId == profissionalId)
            .ToListAsync(cancellationToken);

        var servicoIds = vinculos.Select(v => v.ServicoId).ToList();
        var servicos = await _dbContext.Servicos.AsNoTracking()
            .Where(s => servicoIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        return vinculos
            .Where(v => servicos.ContainsKey(v.ServicoId))
            .Select(v =>
            {
                var servico = servicos[v.ServicoId];
                return new ProfissionalServicoResumo(
                    servico.Id, servico.Nome,
                    v.PrecoPersonalizado ?? servico.Preco,
                    v.DuracaoPersonalizadaMinutos ?? servico.DuracaoMinutos);
            })
            .ToList();
    }

    public async Task VincularAsync(
        Guid profissionalId, Guid servicoId, decimal? precoPersonalizado, int? duracaoPersonalizadaMinutos,
        CancellationToken cancellationToken = default)
    {
        var existente = await _dbContext.ProfissionalServicos
            .FirstOrDefaultAsync(ps => ps.ProfissionalId == profissionalId && ps.ServicoId == servicoId, cancellationToken);

        if (existente is not null)
        {
            _dbContext.ProfissionalServicos.Remove(existente);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var vinculo = ProfissionalServico.Criar(
            _contextoNegocio.NegocioId!.Value, profissionalId, servicoId, precoPersonalizado, duracaoPersonalizadaMinutos);
        _dbContext.ProfissionalServicos.Add(vinculo);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DesvincularAsync(Guid profissionalId, Guid servicoId, CancellationToken cancellationToken = default)
    {
        var vinculo = await _dbContext.ProfissionalServicos
            .FirstOrDefaultAsync(ps => ps.ProfissionalId == profissionalId && ps.ServicoId == servicoId, cancellationToken);

        if (vinculo is null)
            return false;

        _dbContext.ProfissionalServicos.Remove(vinculo);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
