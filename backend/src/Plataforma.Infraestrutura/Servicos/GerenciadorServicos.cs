using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Dominio.Servicos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Servicos;

public sealed class GerenciadorServicos : IGerenciadorServicos
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public GerenciadorServicos(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<ServicoResumo>> ListarAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Servicos
            .OrderBy(s => s.Nome)
            .Select(s => new ServicoResumo(s.Id, s.CategoriaId, s.Nome, s.Preco, s.DuracaoMinutos, s.Popular, s.Ativo))
            .ToListAsync(cancellationToken);

    public async Task<Guid> CriarAsync(CriarServico dados, CancellationToken cancellationToken = default)
    {
        var servico = Servico.Criar(
            _contextoNegocio.NegocioId!.Value, dados.CategoriaId, dados.Nome, dados.Preco, dados.DuracaoMinutos, dados.Popular);

        _dbContext.Servicos.Add(servico);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return servico.Id;
    }

    public async Task<bool> AtualizarAsync(Guid servicoId, AtualizarServico dados, CancellationToken cancellationToken = default)
    {
        var servico = await _dbContext.Servicos.FindAsync([servicoId], cancellationToken);
        if (servico is null)
            return false;

        servico.AtualizarDados(dados.CategoriaId, dados.Nome, dados.Preco, dados.DuracaoMinutos, dados.Popular);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DesativarAsync(Guid servicoId, CancellationToken cancellationToken = default) =>
        await AlterarAtivoAsync(servicoId, ativo: false, cancellationToken);

    public async Task<bool> AtivarAsync(Guid servicoId, CancellationToken cancellationToken = default) =>
        await AlterarAtivoAsync(servicoId, ativo: true, cancellationToken);

    private async Task<bool> AlterarAtivoAsync(Guid servicoId, bool ativo, CancellationToken cancellationToken)
    {
        var servico = await _dbContext.Servicos.FindAsync([servicoId], cancellationToken);
        if (servico is null)
            return false;

        if (ativo)
            servico.Ativar();
        else
            servico.Desativar();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
