using Plataforma.Aplicacao.Idempotencia;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Fidelidade;
using Plataforma.Dominio.Fidelidade;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Fidelidade;

public sealed class GerenciadorFidelidade : IGerenciadorFidelidade
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    private readonly IServicoIdempotencia _idempotencia;

    public GerenciadorFidelidade(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IServicoIdempotencia idempotencia)
    {
        _idempotencia = idempotencia;
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<ProgramaFidelidadeResumo?> ObterProgramaAsync(CancellationToken cancellationToken = default)
    {
        var programa = await ObterProgramaDoNegocioAsync(cancellationToken);
        return programa is null ? null : new ProgramaFidelidadeResumo(programa.SelosNecessarios, programa.DescricaoRecompensa, programa.Ativo);
    }

    public async Task DefinirProgramaAsync(DefinirProgramaFidelidade dados, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var programa = await ObterProgramaDoNegocioAsync(cancellationToken);

        if (programa is null)
        {
            programa = ProgramaFidelidade.Criar(negocioId, dados.SelosNecessarios, dados.DescricaoRecompensa);
            _dbContext.ProgramasFidelidade.Add(programa);
        }
        else
        {
            programa.AtualizarCondicoes(dados.SelosNecessarios, dados.DescricaoRecompensa);
            programa.Ativar();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RegistrarSeloAsync(Guid clienteId, Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var programa = await ObterProgramaDoNegocioAsync(cancellationToken);
        if (programa is null || !programa.Ativo)
            return;

        var jaTemSelo = await _dbContext.SelosCliente.AnyAsync(s => s.AgendamentoId == agendamentoId, cancellationToken);
        if (jaTemSelo)
            return;

        _dbContext.SelosCliente.Add(SeloCliente.Criar(programa.NegocioId, clienteId, agendamentoId));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProgressoFidelidade> ObterProgressoAsync(Guid clienteId, CancellationToken cancellationToken = default)
    {
        var programa = await ObterProgramaDoNegocioAsync(cancellationToken);
        var selosAtuais = await _dbContext.SelosCliente
            .CountAsync(s => s.ClienteId == clienteId && !s.Resgatado, cancellationToken);

        if (programa is null || !programa.Ativo)
            return new ProgressoFidelidade(selosAtuais, 0, false, null);

        return new ProgressoFidelidade(
            selosAtuais, programa.SelosNecessarios, selosAtuais >= programa.SelosNecessarios, programa.DescricaoRecompensa);
    }

    public async Task<bool> ResgatarRecompensaAsync(Guid clienteId, CancellationToken cancellationToken = default)
    {
        // Com Idempotency-Key o resgate roda numa transação que reivindica a chave: o segundo clique não consome outro lote de
        // selos. Sem chave o caminho é o de sempre (um SaveChanges): repetir o callback inteiro após um COMMIT ambíguo
        // resgataria outra recompensa, então a estratégia de callback só entra quando há a chave que o protege.
        if (_idempotencia.Ativa)
            return await ResgatarComChaveAsync(clienteId, cancellationToken);

        return await ResgatarAsync(clienteId, cancellationToken);
    }

    private async Task<bool> ResgatarComChaveAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        var tentativas = new TentativasDaTransacao(_dbContext);
        return await estrategia.ExecuteAsync(async () =>
        {
            tentativas.Iniciar();
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (await _idempotencia.ReivindicarAsync<ResultadoConcluido>(cancellationToken) is not null)
                return true;

            if (!await ResgatarAsync(clienteId, cancellationToken))
                return false;

            await _idempotencia.ConcluirAsync(new ResultadoConcluido(), cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return true;
        });
    }

    private async Task<bool> ResgatarAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        var programa = await ObterProgramaDoNegocioAsync(cancellationToken);
        if (programa is null || !programa.Ativo)
            return false;

        var selosDisponiveis = await _dbContext.SelosCliente
            .Where(s => s.ClienteId == clienteId && !s.Resgatado)
            .OrderBy(s => s.CriadoEm)
            .Take(programa.SelosNecessarios)
            .ToListAsync(cancellationToken);

        if (selosDisponiveis.Count < programa.SelosNecessarios)
            return false;

        foreach (var selo in selosDisponiveis)
            selo.MarcarResgatado();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<ProgramaFidelidade?> ObterProgramaDoNegocioAsync(CancellationToken cancellationToken) =>
        _dbContext.ProgramasFidelidade.FirstOrDefaultAsync(p => p.NegocioId == _contextoNegocio.NegocioId, cancellationToken);
}
