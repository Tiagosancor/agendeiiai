using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Plataforma.Infraestrutura.Persistencia;

/// <summary>
/// Detecta violação de índice único do Postgres dentro de um <see cref="DbUpdateException"/>.
/// Usado para converter uma corrida (duas requisições simultâneas passam pela checagem
/// "já existe?" antes de qualquer uma delas inserir) na exceção de domínio certa, em vez
/// de deixar vazar um 500 com detalhe de banco (seção 8.2.4 — mesmo espírito, fora do
/// contexto de horário).
/// </summary>
public static class ExcecoesDeConcorrencia
{
    public static bool EhViolacaoDeUnicidade(this DbUpdateException excecao) =>
        excecao.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>Violação da exclusion constraint de horários (seção 8.2.1) — dois agendamentos do mesmo profissional se sobrepondo.</summary>
    public static bool EhViolacaoDeExclusao(this DbUpdateException excecao) =>
        excecao.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation };

    /// <summary>
    /// COMMIT ambíguo (B7.2) num <c>SaveChanges</c> avulso: o INSERT já tinha sido confirmado e a repetição da estratégia, com o
    /// mesmo Id (gerado na criação da entidade, uma vez por requisição), bate na chave primária. Se existe linha com esse Id, a
    /// colisão é a da própria requisição e a criação deve ser tratada como sucesso; colisão de outra requisição (e-mail, telefone)
    /// continua sendo erro. O contexto é limpo: o que sobrou rastreado é da tentativa que acabou de falhar.
    /// </summary>
    public static async Task<bool> CriacaoJaConfirmadaAsync<T>(
        this DbContext dbContext, DbUpdateException excecao, Guid id, CancellationToken cancellationToken)
        where T : Plataforma.Dominio.Comum.EntidadeBase
    {
        if (!excecao.EhViolacaoDeUnicidade())
            return false;

        dbContext.ChangeTracker.Clear();
        return await dbContext.Set<T>().AsNoTracking().AnyAsync(e => e.Id == id, cancellationToken);
    }
}
