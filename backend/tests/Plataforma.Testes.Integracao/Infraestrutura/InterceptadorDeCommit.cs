using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>
/// Simula a perda de conexão em volta do <c>COMMIT</c> de uma transação do EF, com uma <see cref="NpgsqlException"/> transitória
/// (a que a <c>ExecutionStrategy</c> repete). Dois momentos, ambos determinísticos (sem tempo de espera):
/// <list type="bullet">
/// <item><b>Ambíguo</b> — depois de o servidor confirmar: o dado está gravado, mas a aplicação recebe um erro (a resposta se perdeu).</item>
/// <item><b>Antes do commit</b> — nada foi confirmado: a transação é desfeita e a repetição precisa gravar.</item>
/// </list>
/// Só transações explícitas (<c>BeginTransactionAsync</c>) passam por aqui; o <c>SaveChanges</c> avulso é coberto pelo proxy.
/// </summary>
public sealed class InterceptadorDeCommit : DbTransactionInterceptor
{
    private readonly object _trava = new();
    private int _ambiguos;
    private int _antes;
    private bool _transitorio = true;

    /// <summary>Commits que chegaram a ser confirmados pelo servidor (inclui os de resposta "perdida").</summary>
    public int Commits { get; private set; }

    /// <summary>Quantas falhas simuladas foram de fato lançadas.</summary>
    public int Falhas { get; private set; }

    public void ArmarCommitAmbiguo(int vezes = 1)
    {
        lock (_trava)
        {
            (_ambiguos, _antes, _transitorio, Commits, Falhas) = (vezes, 0, true, 0, 0);
        }
    }

    public void ArmarFalhaAntesDoCommit(int vezes = 1, bool transitoria = true)
    {
        lock (_trava)
        {
            (_ambiguos, _antes, _transitorio, Commits, Falhas) = (0, vezes, transitoria, 0, 0);
        }
    }

    public void Desarmar() => ArmarCommitAmbiguo(0);

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        lock (_trava)
        {
            if (_antes > 0)
            {
                _antes--;
                Falhas++;
                throw _transitorio
                    ? new NpgsqlException("Simulado: conexão perdida antes do COMMIT.", new IOException("Conexão encerrada."))
                    : new ApplicationException("Simulado: falha definitiva antes do COMMIT.");
            }
        }

        return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        lock (_trava)
        {
            Commits++;
            if (_ambiguos > 0)
            {
                _ambiguos--;
                Falhas++;
                throw new NpgsqlException("Simulado: a resposta do COMMIT se perdeu depois de o servidor confirmá-lo.", new IOException("Conexão encerrada."));
            }
        }

        return Task.CompletedTask;
    }
}
