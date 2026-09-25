using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Seguranca;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Profissionais;

public sealed class GerenciadorProfissionais : IGerenciadorProfissionais
{
    private readonly PlataformaDbContext _dbContext;
    private readonly ICriptografiaCpf _criptografiaCpf;
    private readonly IContextoNegocio _contextoNegocio;

    public GerenciadorProfissionais(
        PlataformaDbContext dbContext, ICriptografiaCpf criptografiaCpf, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _criptografiaCpf = criptografiaCpf;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<ProfissionalResumo>> ListarAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Profissionais
            .OrderBy(p => p.Nome)
            .Select(p => new ProfissionalResumo(p.Id, p.Nome, p.Ativo, p.FotoUrl, p.Funcao))
            .ToListAsync(cancellationToken);

    public async Task<ProfissionalDetalhe?> ObterAsync(Guid profissionalId, CancellationToken cancellationToken = default)
    {
        var profissional = await _dbContext.Profissionais.FindAsync([profissionalId], cancellationToken);
        return profissional is null ? null : Mapear(profissional);
    }

    public async Task<Guid> CriarAsync(CriarProfissional dados, CancellationToken cancellationToken = default)
    {
        CpfProtegido? cpfProtegido = string.IsNullOrWhiteSpace(dados.Cpf)
            ? null
            : _criptografiaCpf.Proteger(Cpf.Criar(dados.Cpf));

        var profissional = Profissional.Criar(
            _contextoNegocio.NegocioId!.Value, dados.Nome, dados.Telefone, dados.Email, cpf: cpfProtegido, funcao: dados.Funcao);

        await DentroDoLimiteDoPlanoAsync(async () =>
        {
            _dbContext.Profissionais.Add(profissional);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return profissional.Id;
    }

    public async Task<bool> AtualizarDadosAsync(
        Guid profissionalId, AtualizarProfissional dados, CancellationToken cancellationToken = default)
    {
        var profissional = await _dbContext.Profissionais.FindAsync([profissionalId], cancellationToken);
        if (profissional is null)
            return false;

        profissional.AtualizarDados(dados.Nome, dados.Telefone, dados.Email, profissional.Endereco, dados.Funcao);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DesativarAsync(Guid profissionalId, CancellationToken cancellationToken = default) =>
        await AlterarAtivoAsync(profissionalId, ativo: false, cancellationToken);

    public async Task<bool> AtivarAsync(Guid profissionalId, CancellationToken cancellationToken = default) =>
        await AlterarAtivoAsync(profissionalId, ativo: true, cancellationToken);

    public async Task<string?> RevelarCpfAsync(Guid profissionalId, CancellationToken cancellationToken = default)
    {
        var profissional = await _dbContext.Profissionais.FindAsync([profissionalId], cancellationToken);
        return profissional?.Cpf is null ? null : _criptografiaCpf.Revelar(profissional.Cpf);
    }

    private async Task<bool> AlterarAtivoAsync(Guid profissionalId, bool ativo, CancellationToken cancellationToken)
    {
        var profissional = await _dbContext.Profissionais.FindAsync([profissionalId], cancellationToken);
        if (profissional is null)
            return false;

        if (!ativo)
        {
            profissional.Desativar();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (profissional.Ativo)
            return true;

        await DentroDoLimiteDoPlanoAsync(async () =>
        {
            profissional.Ativar();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return true;
    }

    /// <summary>
    /// Limite de profissionais ativos do plano (seção 7). Trava a linha da assinatura
    /// (FOR UPDATE) antes de contar: dois cadastros simultâneos no último lugar livre não
    /// passam os dois. Sem assinatura (só em testes antigos), não há limite.
    /// </summary>
    private async Task DentroDoLimiteDoPlanoAsync(Func<Task> acao, CancellationToken cancellationToken)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM assinaturas WHERE negocio_id = {negocioId} FOR UPDATE", cancellationToken);

            var plano = await (
                from a in _dbContext.Assinaturas
                join p in _dbContext.Planos on a.PlanoId equals p.Id
                select new { p.Nome, p.MaximoProfissionais }).FirstOrDefaultAsync(cancellationToken);

            if (plano is not null)
            {
                var ativos = await _dbContext.Profissionais.CountAsync(p => p.Ativo, cancellationToken);
                if (ativos + 1 > plano.MaximoProfissionais)
                    throw new LimitePlanoAtingidoException(plano.Nome, plano.MaximoProfissionais);
            }

            await acao();
            await transacao.CommitAsync(cancellationToken);
        });
    }

    private static ProfissionalDetalhe Mapear(Profissional profissional) => new(
        profissional.Id, profissional.Nome, profissional.Telefone, profissional.Email, profissional.Ativo,
        profissional.FotoUrl, profissional.Cpf?.Mascarado, profissional.Funcao);
}
