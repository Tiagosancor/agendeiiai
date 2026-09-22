using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Profissionais;
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
            .Select(p => new ProfissionalResumo(p.Id, p.Nome, p.Ativo, p.FotoUrl))
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
            _contextoNegocio.NegocioId!.Value, dados.Nome, dados.Telefone, dados.Email, cpf: cpfProtegido);

        _dbContext.Profissionais.Add(profissional);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return profissional.Id;
    }

    public async Task<bool> AtualizarDadosAsync(
        Guid profissionalId, AtualizarProfissional dados, CancellationToken cancellationToken = default)
    {
        var profissional = await _dbContext.Profissionais.FindAsync([profissionalId], cancellationToken);
        if (profissional is null)
            return false;

        profissional.AtualizarDados(dados.Nome, dados.Telefone, dados.Email, profissional.Endereco);
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

        if (ativo)
            profissional.Ativar();
        else
            profissional.Desativar();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ProfissionalDetalhe Mapear(Profissional profissional) => new(
        profissional.Id, profissional.Nome, profissional.Telefone, profissional.Email, profissional.Ativo,
        profissional.FotoUrl, profissional.Cpf?.Mascarado);
}
