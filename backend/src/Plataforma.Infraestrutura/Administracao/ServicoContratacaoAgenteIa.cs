using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Administracao;
using Plataforma.Dominio.Administracao;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Administracao;

/// <summary>
/// ÚNICO lugar que escreve a contratação do agente de IA do WhatsApp (<c>Negocio.AgenteIaContratado</c>). Hoje só o administrador da
/// plataforma (origem <see cref="OrigemAgenteIa.Manual"/>); a cobrança (plano que inclui, adicional pelo Asaas) entra aqui depois,
/// de modo que o runtime continue lendo um campo só. Usado por <see cref="AdministracaoPlataforma"/>, que já é o módulo autorizado a
/// enxergar vários negócios — por isso o negócio vem do id da rota e não há contexto de tenant.
/// </summary>
public sealed class ServicoContratacaoAgenteIa
{
    public const string AcaoLigar = "LigarAgenteIa";
    public const string AcaoDesligar = "DesligarAgenteIa";
    private const int LimiteHistorico = 100;

    private readonly PlataformaDbContext _dbContext;

    public ServicoContratacaoAgenteIa(PlataformaDbContext dbContext) => _dbContext = dbContext;

    public static ServicoAgenteIaNegocio Mapear(Negocio negocio) =>
        new(negocio.AgenteIaContratado, negocio.AgenteIaOrigem, negocio.AgenteIaAlteradoEm, negocio.AgenteIaAlteradoPor);

    /// <summary>
    /// Liga/desliga e audita no mesmo <c>SaveChanges</c>. A linha do negócio é travada (<c>FOR UPDATE</c>) e relida: dois
    /// administradores no mesmo instante são atendidos em fila, e o segundo encontra o estado já novo — repetir o estado atual
    /// devolve-o sem alterar nada e sem nova linha de auditoria.
    /// </summary>
    public async Task<ServicoAgenteIaNegocio?> DefinirAsync(
        string autor, Guid negocioId, bool contratado, string? motivo, CancellationToken cancellationToken = default)
    {
        var motivoLimpo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM negocios WHERE id = {negocioId} FOR UPDATE", cancellationToken);
            var negocio = await _dbContext.Negocios.FirstOrDefaultAsync(n => n.Id == negocioId, cancellationToken);
            if (negocio is null)
            {
                await transacao.RollbackAsync(cancellationToken);
                return null;
            }

            var antes = negocio.AgenteIaOrigem;
            var agora = DateTimeOffset.UtcNow;
            var mudou = contratado
                ? negocio.LigarAgenteIa(OrigemAgenteIa.Manual, autor, agora)
                : negocio.DesligarAgenteIa(autor, agora);

            if (mudou)
            {
                var detalhes = contratado
                    ? $"de desligado para ligado; origem {OrigemAgenteIa.Manual}"
                    : $"de ligado para desligado; origem anterior {antes}";
                if (motivoLimpo is not null)
                    detalhes += $"; motivo: {motivoLimpo}";

                _dbContext.LogsAuditoriaPlataforma.Add(new LogAuditoriaPlataforma(
                    autor, contratado ? AcaoLigar : AcaoDesligar, negocioId, detalhes));
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transacao.CommitAsync(cancellationToken);
            return Mapear(negocio);
        });
    }

    public async Task<IReadOnlyList<HistoricoAgenteIa>?> ListarHistoricoAsync(Guid negocioId, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Negocios.AsNoTracking().AnyAsync(n => n.Id == negocioId, cancellationToken))
            return null;

        return await _dbContext.LogsAuditoriaPlataforma.AsNoTracking()
            .Where(l => l.NegocioId == negocioId && (l.Acao == AcaoLigar || l.Acao == AcaoDesligar))
            .OrderByDescending(l => l.CriadoEm)
            .Take(LimiteHistorico)
            .Select(l => new HistoricoAgenteIa(l.CriadoEm, l.Autor, l.Acao, l.Detalhes))
            .ToListAsync(cancellationToken);
    }
}
